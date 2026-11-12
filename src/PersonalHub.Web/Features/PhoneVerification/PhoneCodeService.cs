using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Domain.PhoneVerification;
using PersonalHub.Web.Features.Notifications;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.PhoneVerification;

/// <summary>
/// Para quem é o código: finalidade, celular de destino e o dono (vínculo no convite, conta nos demais).
/// </summary>
public sealed record PhoneCodeScope(PhoneCodePurpose Purpose, string Phone, Guid? UserId = null, Guid? PersonalStudentId = null)
{
    public Guid ScopeId => PersonalStudentId ?? UserId ?? throw new InvalidOperationException("Escopo sem dono.");
}

public enum PhoneCodeSendOutcome
{
    Sent,
    Cooldown,
    Locked
}

public sealed record PhoneCodeSendResult(PhoneCodeSendOutcome Outcome, DateTime RetryAtUtc);

public enum PhoneCodeVerifyOutcome
{
    Verified,

    /// <summary>Campo vazio ou sem 6 dígitos: não conta tentativa.</summary>
    Invalid,

    Wrong,
    Locked,

    /// <summary>Sem código válido: venceu, já foi usado, foi substituído ou nunca foi pedido.</summary>
    Expired
}

public sealed record PhoneCodeVerifyResult(PhoneCodeVerifyOutcome Outcome, int RemainingAttempts = 0, DateTime? LockedUntilUtc = null);

/// <summary>Situação para a tela: há código aberto? quando pode reenviar? está bloqueado?</summary>
public sealed record PhoneCodeStatus(bool HasOpenCode, DateTime? ResendAtUtc, DateTime? LockedUntilUtc);

/// <summary>
/// Envio e conferência dos códigos por WhatsApp (D-056): 6 dígitos, 10 minutos, 5 tentativas, reenvio após 60 s, até 3
/// envios por hora por celular, bloqueio de 15 minutos. O código só existe em texto puro na memória: no banco vai o
/// HMAC (tabela) e a versão protegida pelo Data Protection (outbox, apagada após o envio). Envio e conferência
/// rodam com o celular serializado (advisory lock) e o código travado (<c>FOR UPDATE</c>), então pedidos em paralelo
/// não furam os limites. A auditoria leva só a finalidade e contagens: nunca celular nem código.
/// </summary>
public sealed partial class PhoneCodeService(
    AppDbContext db,
    PhoneCodeHasher hasher,
    OutboxCodeProtector protector,
    TimeProvider clock,
    ILogger<PhoneCodeService> logger)
{
    public async Task<PhoneCodeSendResult> SendAsync(
        PhoneCodeScope scope, Guid? recipientUserId, string? correlationId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockPhoneAsync(scope, cancellationToken);

        var now = Now;
        var recent = await db.PhoneVerificationCodes
            .Where(c => c.PhoneE164 == scope.Phone && c.Purpose == scope.Purpose && c.CreatedAtUtc > now - PhoneCodeRules.SendWindow)
            .Select(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var lockedUntil = await LockedUntilAsync(scope, now, cancellationToken);

        switch (PhoneCodeRules.CanSend(recent, lockedUntil, now, out var retryAt))
        {
            case SendCheck.Cooldown:
                return new PhoneCodeSendResult(PhoneCodeSendOutcome.Cooldown, retryAt);
            case SendCheck.Locked:
                if (lockedUntil is null)
                {
                    await LockForSendsAsync(scope, retryAt, now, correlationId, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }

                LogLocked(logger, scope.Purpose, scope.ScopeId);
                return new PhoneCodeSendResult(PhoneCodeSendOutcome.Locked, retryAt);
        }

        // Só o código mais novo vale.
        await OpenCodes(scope, now).ExecuteUpdateAsync(s => s.SetProperty(c => c.ReplacedAtUtc, now), cancellationToken);

        var code = PhoneCodeHasher.NewCode();
        var row = new PhoneVerificationCode
        {
            Id = Guid.NewGuid(),
            Purpose = scope.Purpose,
            UserId = scope.UserId,
            PersonalStudentId = scope.PersonalStudentId,
            PhoneE164 = scope.Phone,
            CodeHash = hasher.Hash(scope.Purpose, scope.ScopeId, code),
            ExpiresAtUtc = now + PhoneCodeRules.Validity,
            CreatedAtUtc = now
        };
        db.PhoneVerificationCodes.Add(row);
        await db.EnqueueNotificationAsync(new NotificationRequest(
            NotificationTemplates.Code, $"phone-code:{row.Id}", recipientUserId, scope.Phone,
            new NotificationPayload([], null, protector.Protect(code)),
            IsAuthentication: true, ExpiresAtUtc: row.ExpiresAtUtc), now, cancellationToken);
        Audit("PhoneCodeSent", row, now, correlationId, new { purpose = scope.Purpose.ToString() });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogSent(logger, scope.Purpose, scope.ScopeId, row.Id);
        return new PhoneCodeSendResult(PhoneCodeSendOutcome.Sent, now + PhoneCodeRules.ResendAfter);
    }

    /// <summary>
    /// Confere o código. Usa a transação aberta pelo chamador, se houver (a troca de senha consome o código e troca a
    /// senha juntos); senão abre e confirma a própria. Uma tentativa errada é gravada mesmo que o chamador desista:
    /// quem chama confirma a transação também no erro.
    /// </summary>
    public async Task<PhoneCodeVerifyResult> VerifyAsync(
        PhoneCodeScope scope, string? input, string? correlationId, CancellationToken cancellationToken)
    {
        var code = PhoneCodeRules.Normalize(input);
        if (code is null)
        {
            return new PhoneCodeVerifyResult(PhoneCodeVerifyOutcome.Invalid);
        }

        IDbContextTransaction? own = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var result = await VerifyInTransactionAsync(scope, code, correlationId, cancellationToken);
            if (own is not null)
            {
                await own.CommitAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync();
            }
        }
    }

    public async Task<PhoneCodeStatus> GetStatusAsync(PhoneCodeScope scope, CancellationToken cancellationToken)
    {
        var now = Now;
        var hasOpen = await OpenCodes(scope, now).AnyAsync(cancellationToken);
        var lastSend = await db.PhoneVerificationCodes
            .Where(c => c.PhoneE164 == scope.Phone && c.Purpose == scope.Purpose)
            .MaxAsync(c => (DateTime?)c.CreatedAtUtc, cancellationToken);
        var resendAt = lastSend + PhoneCodeRules.ResendAfter;
        return new PhoneCodeStatus(hasOpen, resendAt > now ? resendAt : null, await LockedUntilAsync(scope, now, cancellationToken));
    }

    private async Task<PhoneCodeVerifyResult> VerifyInTransactionAsync(
        PhoneCodeScope scope, string code, string? correlationId, CancellationToken cancellationToken)
    {
        var now = Now;
        var purpose = scope.Purpose.ToString();
        var scopeId = scope.ScopeId;
        // xmin fica fora do * e o EF precisa dele como token de concorrência.
        var rows = await db.PhoneVerificationCodes
            .FromSqlInterpolated($"""
                SELECT c.*, c.xmin FROM "PhoneVerificationCode" c
                WHERE c."Purpose" = {purpose} AND c."PhoneE164" = {scope.Phone}
                  AND COALESCE(c."PersonalStudentId", c."UserId") = {scopeId}
                  AND c."ConsumedAtUtc" IS NULL AND c."ReplacedAtUtc" IS NULL
                ORDER BY c."CreatedAtUtc" DESC
                LIMIT 1
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        var row = rows.SingleOrDefault();

        if (await LockedUntilAsync(scope, now, cancellationToken) is { } lockedUntil)
        {
            return new PhoneCodeVerifyResult(PhoneCodeVerifyOutcome.Locked, LockedUntilUtc: lockedUntil);
        }

        if (row is null || !row.IsOpen(now))
        {
            return new PhoneCodeVerifyResult(PhoneCodeVerifyOutcome.Expired);
        }

        if (hasher.Matches(row.CodeHash, scope.Purpose, scopeId, code))
        {
            row.ConsumedAtUtc = now;
            Audit("PhoneCodeVerified", row, now, correlationId, new { purpose, attempts = row.Attempts + 1 });
            await db.SaveChangesAsync(cancellationToken);
            LogVerified(logger, scope.Purpose, scopeId, row.Id);
            return new PhoneCodeVerifyResult(PhoneCodeVerifyOutcome.Verified);
        }

        row.Attempts++;
        var locked = row.Attempts >= PhoneCodeRules.MaxAttempts;
        if (locked)
        {
            row.LockedUntilUtc = now + PhoneCodeRules.LockDuration;
        }

        Audit(locked ? "PhoneCodeLocked" : "PhoneCodeFailed", row, now, correlationId,
            new { purpose, attempts = row.Attempts, reason = locked ? "tentativas" : null });
        await db.SaveChangesAsync(cancellationToken);
        LogWrong(logger, scope.Purpose, scopeId, row.Attempts);

        return locked
            ? new PhoneCodeVerifyResult(PhoneCodeVerifyOutcome.Locked, LockedUntilUtc: row.LockedUntilUtc)
            : new PhoneCodeVerifyResult(PhoneCodeVerifyOutcome.Wrong, PhoneCodeRules.MaxAttempts - row.Attempts);
    }

    /// <summary>Pediu o 4º código na hora: grava o bloqueio no código mais recente do celular.</summary>
    private async Task LockForSendsAsync(
        PhoneCodeScope scope, DateTime until, DateTime now, string? correlationId, CancellationToken cancellationToken)
    {
        var latest = await db.PhoneVerificationCodes
            .Where(c => c.PhoneE164 == scope.Phone && c.Purpose == scope.Purpose)
            .OrderByDescending(c => c.CreatedAtUtc)
            .FirstAsync(cancellationToken);
        latest.LockedUntilUtc = until;
        Audit("PhoneCodeLocked", latest, now, correlationId, new { purpose = scope.Purpose.ToString(), reason = "envios" });
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<DateTime?> LockedUntilAsync(PhoneCodeScope scope, DateTime now, CancellationToken cancellationToken) =>
        db.PhoneVerificationCodes
            .Where(c => c.PhoneE164 == scope.Phone && c.Purpose == scope.Purpose && c.LockedUntilUtc > now)
            .MaxAsync(c => c.LockedUntilUtc, cancellationToken);

    private IQueryable<PhoneVerificationCode> OpenCodes(PhoneCodeScope scope, DateTime now) =>
        db.PhoneVerificationCodes.Where(c =>
            c.Purpose == scope.Purpose && c.PhoneE164 == scope.Phone &&
            c.UserId == scope.UserId && c.PersonalStudentId == scope.PersonalStudentId &&
            c.ConsumedAtUtc == null && c.ReplacedAtUtc == null && c.ExpiresAtUtc > now);

    /// <summary>Serializa envios e conferências do mesmo celular e finalidade até o fim da transação.</summary>
    private Task LockPhoneAsync(PhoneCodeScope scope, CancellationToken cancellationToken)
    {
        var key = $"phone-code:{scope.Purpose}:{scope.Phone}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({key}))", cancellationToken);
    }

    private void Audit(string action, PhoneVerificationCode row, DateTime now, string? correlationId, object metadata) =>
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = row.UserId,
            Action = action,
            EntityType = nameof(PhoneVerificationCode),
            EntityId = row.Id,
            OccurredAtUtc = now,
            CorrelationId = correlationId,
            Metadata = JsonSerializer.Serialize(metadata),
            CreatedAtUtc = now
        });

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [LoggerMessage(EventId = 7300, Level = LogLevel.Information,
        Message = "Código {CodeId} ({Purpose}) enfileirado para {ScopeId}")]
    private static partial void LogSent(ILogger logger, PhoneCodePurpose purpose, Guid scopeId, Guid codeId);

    [LoggerMessage(EventId = 7301, Level = LogLevel.Information,
        Message = "Código {CodeId} ({Purpose}) confirmado para {ScopeId}")]
    private static partial void LogVerified(ILogger logger, PhoneCodePurpose purpose, Guid scopeId, Guid codeId);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Warning,
        Message = "Código incorreto ({Purpose}) para {ScopeId}: tentativa {Attempts}")]
    private static partial void LogWrong(ILogger logger, PhoneCodePurpose purpose, Guid scopeId, int attempts);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Warning,
        Message = "Envio de código ({Purpose}) bloqueado para {ScopeId}")]
    private static partial void LogLocked(ILogger logger, PhoneCodePurpose purpose, Guid scopeId);
}
