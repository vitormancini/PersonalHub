using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Billing.PersonalBilling;

public sealed record PersonalStatementRow(
    Guid Id, string StudentName, DateOnly ReferenceMonth, DateOnly DueDate, int LessonCount, decimal TotalAmount,
    BillingPeriodStatus Status);

/// <summary>Vínculo cujo fechamento ainda vai acontecer no mês corrente, com a prévia.</summary>
public sealed record UpcomingClosingRow(Guid PersonalStudentId, string StudentName, DateOnly ClosingDate, int LessonCount, decimal PreviewTotal);

public sealed record PersonalBillingView(
    IReadOnlyList<PersonalStatementRow> Statements,
    IReadOnlyList<UpcomingClosingRow> Upcoming)
{
    public decimal Billed => Statements.Sum(s => s.TotalAmount);

    public decimal Received => Statements.Where(s => s.Status == BillingPeriodStatus.Paid).Sum(s => s.TotalAmount);

    public decimal Receivable => Billed - Received;

    public int LessonCount => Statements.Sum(s => s.LessonCount);
}

/// <summary>Demonstrativos dos vínculos do personal logado que vencem num mês, e o que ainda vai fechar no mês corrente.</summary>
public sealed class PersonalBillingQuery(AppDbContext db, TimeProvider clock)
{
    private static readonly StringComparer NameOrder = StringComparer.Create(StudentDisplay.PtBr, CompareOptions.IgnoreCase);

    public async Task<PersonalBillingView> GetAsync(Guid personalUserId, DateOnly month, CancellationToken cancellationToken)
    {
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var today = SaoPaulo.ToLocalDate(nowUtc);
        var first = new DateOnly(month.Year, month.Month, 1);
        var next = first.AddMonths(1);

        var statements = await db.BillingPeriods
            .Where(p => p.DueDate >= first && p.DueDate < next)
            .Join(OwnedLinks(personalUserId), p => p.PersonalStudentId, s => s.Id, (p, s) => new { p, s.StudentName })
            .OrderBy(x => x.p.DueDate).ThenBy(x => x.StudentName)
            .Select(x => new PersonalStatementRow(x.p.Id, x.StudentName, x.p.ReferenceMonth, x.p.DueDate,
                db.BillingPeriodItems.Count(i => i.BillingPeriodId == x.p.Id && i.Kind == BillingPeriodItemKind.Lesson),
                x.p.TotalAmount, x.p.Status))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var upcoming = first == new DateOnly(today.Year, today.Month, 1)
            ? await UpcomingAsync(personalUserId, today, nowUtc, cancellationToken)
            : [];

        return new PersonalBillingView(statements, upcoming);
    }

    private async Task<List<UpcomingClosingRow>> UpcomingAsync(
        Guid personalUserId, DateOnly today, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var links = await OwnedLinks(personalUserId)
            .Select(s => new { s.Id, s.StudentName, s.BillingDueDay, s.Status })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Fecha ainda neste mês quem tem o vencimento depois de hoje.
        var closingThisMonth = links
            .Where(l => BillingClosing.NextClosing(today, l.BillingDueDay).ClosingDate is var d
                        && d.Year == today.Year && d.Month == today.Month)
            .ToList();

        var previews = await db.NextClosingPreviewsAsync(
            closingThisMonth.Select(l => new PreviewLink(l.Id, l.BillingDueDay)).ToList(), nowUtc, cancellationToken);

        return closingThisMonth
            // Inativo só aparece se ainda tem algo a fechar (D-046).
            .Where(l => l.Status != PersonalStudentStatus.Inactive || previews[l.Id].LessonCount > 0)
            .Select(l => new UpcomingClosingRow(l.Id, l.StudentName, previews[l.Id].ClosingDate, previews[l.Id].LessonCount,
                previews[l.Id].Total))
            .OrderBy(r => r.ClosingDate).ThenBy(r => r.StudentName, NameOrder)
            .ToList();
    }

    private IQueryable<PersonalStudent> OwnedLinks(Guid personalUserId) =>
        db.PersonalStudents
            .Join(db.PersonalProfiles.Where(p => p.UserId == personalUserId && p.IsActive),
                s => s.PersonalId, p => p.Id, (s, p) => s);
}
