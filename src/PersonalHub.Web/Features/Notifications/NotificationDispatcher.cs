using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Infrastructure.Persistence;
using PersonalHub.Web.Infrastructure.WhatsApp;

namespace PersonalHub.Web.Features.Notifications;

public sealed class NotificationWorkerOptions
{
    public const string Section = "Notifications:Worker";

    /// <summary>Desligado nos testes, que chamam o dispatcher diretamente.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);
    public int BatchSize { get; set; } = 20;

    /// <summary>Prazo de quem pegou a linha. Vencido (processo caiu no meio do envio), outro worker retoma a linha.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromMinutes(2);
}

/// <summary>Resumo de uma rodada do worker.</summary>
public sealed record DispatchResult(int Claimed, int Sent, int Retried, int Failed);

/// <summary>
/// Processa a outbox (ARCHITECTURE §6). Pega um lote com <c>FOR UPDATE SKIP LOCKED</c> e marca <c>Processing</c> com
/// prazo (lease) numa transação curta; envia fora da transação; grava o resultado com concorrência otimista (xmin).
/// Duas instâncias nunca pegam a mesma linha, e uma linha presa num processo que caiu volta depois que o prazo vence.
/// A tentativa é contada ao pegar a linha, então uma linha que derruba o processo também esgota as tentativas.
/// </summary>
public sealed partial class NotificationDispatcher(
    AppDbContext db,
    IWhatsAppSender sender,
    OutboxCodeProtector codes,
    TimeProvider clock,
    IOptions<NotificationWorkerOptions> options,
    ILogger<NotificationDispatcher> logger)
{
    public async Task<DispatchResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var claimed = await ClaimAsync(cancellationToken);
        int sent = 0, retried = 0, failed = 0;
        foreach (var row in claimed)
        {
            switch (await ProcessAsync(row, cancellationToken))
            {
                case NotificationStatus.Sent:
                    sent++;
                    break;
                case NotificationStatus.Pending:
                    retried++;
                    break;
                case NotificationStatus.Failed:
                    failed++;
                    break;
            }
        }

        db.ChangeTracker.Clear();
        if (claimed.Count > 0)
        {
            LogRun(logger, claimed.Count, sent, retried, failed);
        }

        return new DispatchResult(claimed.Count, sent, retried, failed);
    }

    /// <summary>Pega as linhas devidas (ou com prazo vencido) que nenhuma outra transação travou.</summary>
    public async Task<IReadOnlyList<NotificationOutbox>> ClaimAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = Now;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await db.NotificationOutbox
            .FromSqlInterpolated($"""
                SELECT o.*, o.xmin FROM "NotificationOutbox" o
                WHERE (o."Status" = 'Pending' AND o."NextAttemptAtUtc" <= {now})
                   OR (o."Status" = 'Processing' AND o."LeaseExpiresAtUtc" <= {now})
                ORDER BY o."NextAttemptAtUtc"
                LIMIT {settings.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            if (row.Status == NotificationStatus.Processing)
            {
                LogReclaimed(logger, row.Id, row.Attempts);
            }

            row.Status = NotificationStatus.Processing;
            row.LeaseExpiresAtUtc = now + settings.Lease;
            row.Attempts++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    private async Task<NotificationStatus?> ProcessAsync(NotificationOutbox row, CancellationToken cancellationToken)
    {
        var now = Now;
        var result = await SendAsync(row, now, cancellationToken);

        now = Now;
        row.LeaseExpiresAtUtc = null;
        switch (result.Status)
        {
            case WhatsAppSendStatus.Sent:
                row.Status = NotificationStatus.Sent;
                row.SentAtUtc = now;
                row.ProviderMessageId = result.ProviderMessageId;
                row.DeliveryStatus = NotificationDeliveryStatus.Sent;
                row.LastError = null;
                ClearSecrets(row);
                break;
            case WhatsAppSendStatus.Retryable when NextAttempt(row, now) is { } next:
                row.Status = NotificationStatus.Pending;
                row.NextAttemptAtUtc = next;
                row.LastError = result.Error;
                break;
            default:
                Fail(row, result.Error ?? "erro", now);
                break;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // O prazo venceu e outro worker retomou a linha: o resultado dele vale.
            LogLeaseLost(logger, row.Id);
            db.ChangeTracker.Clear();
            return null;
        }

        if (row.Status == NotificationStatus.Sent)
        {
            LogSent(logger, row.Id, row.EventType, row.Attempts);
        }
        else
        {
            LogNotSent(logger, row.Id, row.EventType, row.Attempts, row.LastError!, row.Status);
        }

        return row.Status;
    }

    private async Task<WhatsAppSendResult> SendAsync(NotificationOutbox row, DateTime now, CancellationToken cancellationToken)
    {
        if (row.Attempts > NotificationSchedule.MaxAttempts)
        {
            return WhatsAppSendResult.Fail("tentativas");
        }

        if (row.ExpiresAtUtc <= now)
        {
            return WhatsAppSendResult.Fail("expirado");
        }

        if (row.RecipientPhone is null)
        {
            return WhatsAppSendResult.Fail("sem-celular");
        }

        var payload = NotificationPayload.FromJson(row.Payload);
        if (payload is null)
        {
            return WhatsAppSendResult.Fail("payload");
        }

        string? code = null;
        if (payload.ProtectedCode is not null && (code = codes.Unprotect(payload.ProtectedCode)) is null)
        {
            return WhatsAppSendResult.Fail("codigo-ilegivel");
        }

        try
        {
            return await sender.SendTemplateAsync(
                new WhatsAppTemplateMessage(row.RecipientPhone, row.EventType, payload.Parameters, payload.ButtonUrlSuffix, code),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogSenderError(logger, ex.GetType().Name, row.Id);
            return WhatsAppSendResult.Retry("erro-interno");
        }
    }

    /// <summary>
    /// Próxima tentativa (1 min, 5 min, 30 min, 2 h), respeitando o silêncio para avisos comuns. Nulo quando as
    /// tentativas acabaram ou o código vence antes da próxima.
    /// </summary>
    private static DateTime? NextAttempt(NotificationOutbox row, DateTime now)
    {
        if (NotificationSchedule.RetryDelay(row.Attempts) is not { } delay)
        {
            return null;
        }

        var next = NotificationSchedule.SendAt(now + delay, row.IsAuthentication);
        return row.ExpiresAtUtc is { } expires && next >= expires ? null : next;
    }

    private void Fail(NotificationOutbox row, string reason, DateTime now)
    {
        row.Status = NotificationStatus.Failed;
        row.LastError = reason;
        ClearSecrets(row);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = null,
            Action = "NotificationFailed",
            EntityType = nameof(NotificationOutbox),
            EntityId = row.Id,
            OccurredAtUtc = now,
            Metadata = JsonSerializer.Serialize(new { template = row.EventType, reason, attempts = row.Attempts }),
            CreatedAtUtc = now
        });
    }

    /// <summary>O código protegido não fica em repouso depois que a mensagem saiu ou desistimos dela.</summary>
    private static void ClearSecrets(NotificationOutbox row)
    {
        if (row.IsAuthentication)
        {
            row.Payload = NotificationPayload.Cleared;
        }
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [LoggerMessage(EventId = 7000, Level = LogLevel.Information,
        Message = "Outbox: {Claimed} avisos processados, {Sent} enviados, {Retried} reagendados, {Failed} com falha definitiva")]
    private static partial void LogRun(ILogger logger, int claimed, int sent, int retried, int failed);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Information,
        Message = "Aviso {NotificationId} ({Template}) enviado na tentativa {Attempt}")]
    private static partial void LogSent(ILogger logger, Guid notificationId, string template, int attempt);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning,
        Message = "Aviso {NotificationId} ({Template}) não enviado na tentativa {Attempt}: {Error}; situação {Status}")]
    private static partial void LogNotSent(
        ILogger logger, Guid notificationId, string template, int attempt, string error, NotificationStatus status);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Warning,
        Message = "Aviso {NotificationId} retomado depois do prazo vencido (tentativas até agora: {Attempts})")]
    private static partial void LogReclaimed(ILogger logger, Guid notificationId, int attempts);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Warning,
        Message = "Aviso {NotificationId}: outro worker retomou a linha; resultado desta tentativa descartado")]
    private static partial void LogLeaseLost(ILogger logger, Guid notificationId);

    [LoggerMessage(EventId = 7005, Level = LogLevel.Error,
        Message = "Aviso {NotificationId}: erro inesperado no adapter ({ExceptionType})")]
    private static partial void LogSenderError(ILogger logger, string exceptionType, Guid notificationId);
}
