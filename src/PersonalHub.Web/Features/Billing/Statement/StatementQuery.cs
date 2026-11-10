using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Billing.Statement;

/// <summary>Aula cobrada. <see cref="LateFromMonth"/> é o mês da aula quando ela é anterior ao período (confirmada com atraso).</summary>
public sealed record StatementCharge(
    Guid LessonId, string Description, decimal Amount, DateTime OccurredAtUtc, DateTime? RespondedAtUtc, DateOnly? LateFromMonth);

/// <summary>Crédito: de aula excluída depois de cobrada (<see cref="BilledInMonth"/>) ou saldo do mês anterior.</summary>
public sealed record StatementCredit(Guid? LessonId, string Description, decimal Amount, DateOnly? BilledInMonth);

public sealed record StatementView(
    Guid Id,
    Guid PersonalStudentId,
    string StudentName,
    string PersonalName,
    DateOnly ReferenceMonth,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly DueDate,
    DateTime IssuedAtUtc,
    decimal TotalAmount,
    BillingPeriodStatus Status,
    IReadOnlyList<StatementCharge> Charges,
    IReadOnlyList<StatementCredit> Credits,
    int PendingLessons)
{
    public decimal ChargesTotal => Charges.Sum(c => c.Amount);

    public decimal CreditsTotal => -Credits.Sum(c => c.Amount);

    /// <summary>Crédito que não coube neste demonstrativo e segue para o próximo (D-044).</summary>
    public decimal CarryOut => BillingClosing.CarryOutOf(ChargesTotal - CreditsTotal);

    /// <summary>Preço único das aulas, ou nulo se variou (o resumo mostra só "N aulas").</summary>
    public decimal? UniformPrice => Charges.Select(c => c.Amount).Distinct().Count() == 1 ? Charges[0].Amount : null;

    /// <summary>Último dia do período (o fim gravado é exclusivo).</summary>
    public DateOnly PeriodLastDay => PeriodEnd.AddDays(-1);
}

public sealed record LatestStatement(Guid Id, DateOnly ReferenceMonth, DateOnly DueDate, decimal TotalAmount, BillingPeriodStatus Status);

/// <summary>
/// Demonstrativo para o personal dono do vínculo ou para o aluno do vínculo. Qualquer outro usuário recebe nulo,
/// sem distinguir "não existe" de "não é seu".
/// </summary>
public sealed class StatementQuery(AppDbContext db)
{
    public Task<StatementView?> ForPersonalAsync(Guid personalUserId, Guid billingPeriodId, CancellationToken cancellationToken) =>
        GetAsync(db.PersonalStudents
            .Join(db.PersonalProfiles.Where(p => p.UserId == personalUserId && p.IsActive), s => s.PersonalId, p => p.Id, (s, p) => s),
            billingPeriodId, cancellationToken);

    public Task<StatementView?> ForStudentAsync(Guid studentUserId, Guid billingPeriodId, CancellationToken cancellationToken) =>
        GetAsync(db.PersonalStudents.Where(s => s.StudentUserId == studentUserId), billingPeriodId, cancellationToken);

    /// <summary>Demonstrativo mais recente de um vínculo do personal (detalhe do aluno).</summary>
    public Task<LatestStatement?> LatestForPersonalAsync(Guid personalUserId, Guid personalStudentId, CancellationToken cancellationToken) =>
        db.BillingPeriods
            .Where(p => p.PersonalStudentId == personalStudentId)
            .Join(db.PersonalStudents, p => p.PersonalStudentId, s => s.Id, (p, s) => new { p, s })
            .Join(db.PersonalProfiles.Where(pp => pp.UserId == personalUserId && pp.IsActive), x => x.s.PersonalId, pp => pp.Id, (x, pp) => x.p)
            .OrderByDescending(p => p.ReferenceMonth)
            .Select(p => new LatestStatement(p.Id, p.ReferenceMonth, p.DueDate, p.TotalAmount, p.Status))
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<StatementView?> GetAsync(
        IQueryable<Domain.Students.PersonalStudent> allowedLinks, Guid billingPeriodId, CancellationToken cancellationToken)
    {
        var head = await db.BillingPeriods
            .Where(p => p.Id == billingPeriodId)
            .Join(allowedLinks, p => p.PersonalStudentId, s => s.Id, (p, s) => new { p, s })
            .Join(db.PersonalProfiles, x => x.s.PersonalId, pp => pp.Id, (x, pp) => new
            {
                x.p.Id,
                x.p.PersonalStudentId,
                x.s.StudentName,
                PersonalName = pp.DisplayName,
                x.p.ReferenceMonth,
                x.p.PeriodStart,
                x.p.PeriodEnd,
                x.p.DueDate,
                x.p.IssuedAtUtc,
                x.p.TotalAmount,
                x.p.Status
            })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (head is null)
        {
            return null;
        }

        var items = await db.BillingPeriodItems
            .Where(i => i.BillingPeriodId == head.Id)
            .Select(i => new
            {
                i.Kind,
                i.LessonId,
                i.Description,
                i.Amount,
                Lesson = db.Lessons.Where(l => l.Id == i.LessonId)
                    .Select(l => new { l.OccurredAtUtc, l.RespondedAtUtc })
                    .FirstOrDefault(),
                // Mês em que a aula creditada tinha sido cobrada.
                BilledIn = db.BillingPeriodItems
                    .Where(o => i.Kind == BillingPeriodItemKind.Adjustment && o.LessonId == i.LessonId && o.Kind == BillingPeriodItemKind.Lesson)
                    .Join(db.BillingPeriods, o => o.BillingPeriodId, p => p.Id, (o, p) => (DateOnly?)p.ReferenceMonth)
                    .FirstOrDefault()
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var periodStartUtc = SaoPaulo.StartOfDayUtc(head.PeriodStart);
        var charges = items
            .Where(i => i.Kind == BillingPeriodItemKind.Lesson && i.Lesson is not null)
            .OrderBy(i => i.Lesson!.OccurredAtUtc)
            .Select(i => new StatementCharge(i.LessonId!.Value, i.Description, i.Amount, i.Lesson!.OccurredAtUtc,
                i.Lesson.RespondedAtUtc,
                i.Lesson.OccurredAtUtc < periodStartUtc ? FirstOfMonth(SaoPaulo.ToLocalDate(i.Lesson.OccurredAtUtc)) : null))
            .ToList();
        var credits = items
            .Where(i => i.Kind == BillingPeriodItemKind.Adjustment)
            .OrderBy(i => i.LessonId is null).ThenBy(i => i.Lesson?.OccurredAtUtc)
            .Select(i => new StatementCredit(i.LessonId, i.Description, i.Amount, i.BilledIn))
            .ToList();

        var (startUtc, endUtc) = SaoPaulo.MonthRangeUtc(head.ReferenceMonth.Year, head.ReferenceMonth.Month);
        var pending = await db.Lessons.CountAsync(l => l.PersonalStudentId == head.PersonalStudentId
            && l.Status == LessonStatus.Pending && l.DeletedAtUtc == null
            && l.OccurredAtUtc >= startUtc && l.OccurredAtUtc < endUtc, cancellationToken);

        return new StatementView(head.Id, head.PersonalStudentId, head.StudentName, head.PersonalName, head.ReferenceMonth,
            head.PeriodStart, head.PeriodEnd, head.DueDate, head.IssuedAtUtc, head.TotalAmount, head.Status, charges, credits,
            pending);
    }

    private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);
}
