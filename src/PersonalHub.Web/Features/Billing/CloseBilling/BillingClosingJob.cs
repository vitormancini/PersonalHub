using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Billing.CloseBilling;

/// <summary>Resumo de uma execução. <see cref="Ran"/> é falso quando outra instância já estava rodando.</summary>
public sealed record ClosingRunResult(bool Ran, int Due, int Issued, int Empty, int Failed);

public enum CloseLinkOutcome
{
    Issued,

    /// <summary>Sem aula a cobrar nem crédito novo (D-044).</summary>
    Empty,

    /// <summary>Já existe demonstrativo para o mês (ou mais novo): outra execução fechou antes.</summary>
    AlreadyClosed,

    NotFound
}

/// <summary>
/// Fechamento mensal (D-043 a D-047). Para cada vínculo com fechamento devido, emite o demonstrativo numa transação
/// própria, com o vínculo travado. É idempotente: o índice único <c>(PersonalStudentId, ReferenceMonth)</c> e o
/// <c>(LessonId, Kind)</c> garantem um demonstrativo por mês e uma cobrança por aula, mesmo que duas execuções se
/// cruzem; o advisory lock evita que isso aconteça no caso normal (várias instâncias na AWS).
/// </summary>
public sealed partial class BillingClosingJob(AppDbContext db, TimeProvider clock, ILogger<BillingClosingJob> logger)
{
    /// <summary>Chave do advisory lock do Postgres ("PHBILL" em hexadecimal).</summary>
    public const long AdvisoryLockKey = 0x504842494C4C;

    public async Task<ClosingRunResult> RunAsync(CancellationToken cancellationToken)
    {
        // O advisory lock de sessão pertence à conexão: ela fica aberta durante toda a execução.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var acquired = await db.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_lock({AdvisoryLockKey}) AS \"Value\"")
                .SingleAsync(cancellationToken);
            if (!acquired)
            {
                LogLocked(logger);
                return new ClosingRunResult(false, 0, 0, 0, 0);
            }

            try
            {
                return await CloseDueLinksAsync(cancellationToken);
            }
            finally
            {
                await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({AdvisoryLockKey}) AS \"Value\"")
                    .SingleAsync(CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Fecha um mês de um vínculo. Público para os testes de corrida; o caminho normal é <see cref="RunAsync"/>.
    /// </summary>
    public async Task<CloseLinkOutcome> CloseLinkAsync(
        Guid personalStudentId, DateOnly referenceMonth, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Trava o vínculo: duas execuções do mesmo vínculo se enfileiram e a segunda vê o demonstrativo da primeira.
        var dueDay = await db.Database
            .SqlQuery<short>($"""SELECT "BillingDueDay" AS "Value" FROM "PersonalStudent" WHERE "Id" = {personalStudentId} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);
        if (dueDay == 0)
        {
            return CloseLinkOutcome.NotFound;
        }

        if (await db.BillingPeriods.AnyAsync(
                p => p.PersonalStudentId == personalStudentId && p.ReferenceMonth >= referenceMonth, cancellationToken))
        {
            return CloseLinkOutcome.AlreadyClosed;
        }

        var ids = new[] { personalStudentId };
        var (_, monthEndUtc) = SaoPaulo.MonthRangeUtc(referenceMonth.Year, referenceMonth.Month);
        var lessons = await db.ClosingLessonsAsync(ids, monthEndUtc, cancellationToken);
        var carried = await db.CarriedCreditsAsync(ids, cancellationToken);

        var result = BillingClosing.Compose(referenceMonth, dueDay, lessons[personalStudentId],
            carried.GetValueOrDefault(personalStudentId));
        if (result is null)
        {
            return CloseLinkOutcome.Empty;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var period = new BillingPeriod
        {
            Id = Guid.NewGuid(),
            PersonalStudentId = personalStudentId,
            ReferenceMonth = result.ReferenceMonth,
            PeriodStart = result.ReferenceMonth,
            PeriodEnd = result.ReferenceMonth.AddMonths(1),
            DueDate = result.DueDate,
            TotalAmount = result.TotalAmount,
            // Total zero (créditos cobriram tudo) já nasce pago (D-044).
            Status = result.IsZero ? BillingPeriodStatus.Paid : BillingPeriodStatus.Issued,
            IssuedAtUtc = now,
            PaidAtUtc = result.IsZero ? now : null,
            CreatedAtUtc = now
        };
        db.BillingPeriods.Add(period);
        db.BillingPeriodItems.AddRange(result.Items.Select(i => new BillingPeriodItem
        {
            Id = Guid.NewGuid(),
            BillingPeriodId = period.Id,
            Kind = i.Kind,
            LessonId = i.LessonId,
            Description = i.Description,
            Amount = i.Amount,
            CreatedAtUtc = now
        }));

        // Sem nomes, valores de aula nem descrições: só contagens e o mês.
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = null,
            Action = "BillingPeriodIssued",
            EntityType = nameof(BillingPeriod),
            EntityId = period.Id,
            OccurredAtUtc = now,
            CorrelationId = null,
            Metadata = JsonSerializer.Serialize(new
            {
                referenceMonth = result.ReferenceMonth.ToString("yyyy-MM"),
                lessons = result.LessonCount,
                credits = result.Items.Count - result.LessonCount,
                status = period.Status.ToString()
            }),
            CreatedAtUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogIssued(logger, period.Id, personalStudentId, result.ReferenceMonth, result.LessonCount, period.Status);
        return CloseLinkOutcome.Issued;
    }

    private async Task<ClosingRunResult> CloseDueLinksAsync(CancellationToken cancellationToken)
    {
        var today = SaoPaulo.ToLocalDate(clock.GetUtcNow().UtcDateTime);

        var links = await db.PersonalStudents
            .Select(s => new
            {
                s.Id,
                s.BillingDueDay,
                Latest = db.BillingPeriods.Where(p => p.PersonalStudentId == s.Id)
                    .Max(p => (DateOnly?)p.ReferenceMonth)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        int due = 0, issued = 0, empty = 0, failed = 0;
        foreach (var link in links)
        {
            if (BillingClosing.DueReferenceMonth(today, link.BillingDueDay, link.Latest) is not { } month)
            {
                continue;
            }

            due++;
            try
            {
                switch (await CloseLinkAsync(link.Id, month, cancellationToken))
                {
                    case CloseLinkOutcome.Issued:
                        issued++;
                        break;
                    case CloseLinkOutcome.Empty:
                        empty++;
                        break;
                }
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Outra execução gravou primeiro (sem o advisory lock, por exemplo): o demonstrativo já existe.
                LogAlreadyClosed(logger, link.Id, month);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                LogFailed(logger, ex, link.Id, month);
            }
        }

        db.ChangeTracker.Clear();
        LogRun(logger, today, due, issued, empty, failed);
        return new ClosingRunResult(true, due, issued, empty, failed);
    }

    [LoggerMessage(EventId = 5000, Level = LogLevel.Information,
        Message = "Fechamento de {Today}: {Due} vínculos devidos, {Issued} demonstrativos emitidos, {Empty} sem cobrança, {Failed} falhas")]
    private static partial void LogRun(ILogger logger, DateOnly today, int due, int issued, int empty, int failed);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information,
        Message = "Demonstrativo {BillingPeriodId} emitido para o vínculo {PersonalStudentId}, mês {ReferenceMonth}: {Lessons} aulas, situação {Status}")]
    private static partial void LogIssued(
        ILogger logger, Guid billingPeriodId, Guid personalStudentId, DateOnly referenceMonth, int lessons, BillingPeriodStatus status);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information,
        Message = "Fechamento ignorado: outra execução está em andamento")]
    private static partial void LogLocked(ILogger logger);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Information,
        Message = "Vínculo {PersonalStudentId}, mês {ReferenceMonth}: já fechado por outra execução")]
    private static partial void LogAlreadyClosed(ILogger logger, Guid personalStudentId, DateOnly referenceMonth);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Error,
        Message = "Falha ao fechar o vínculo {PersonalStudentId}, mês {ReferenceMonth}")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid personalStudentId, DateOnly referenceMonth);
}
