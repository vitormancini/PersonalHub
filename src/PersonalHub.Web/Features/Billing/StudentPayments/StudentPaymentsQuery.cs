using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Billing.StudentPayments;

public sealed record StudentStatementRow(
    Guid Id, string PersonalName, DateOnly ReferenceMonth, DateOnly DueDate, int LessonCount, decimal TotalAmount,
    BillingPeriodStatus Status);

public sealed record StudentPreviewRow(string PersonalName, DateOnly ReferenceMonth, DateOnly ClosingDate, int LessonCount, decimal Total);

public sealed record StudentPaymentsView(
    IReadOnlyList<StudentStatementRow> Open,
    IReadOnlyList<StudentPreviewRow> Previews,
    IReadOnlyList<StudentStatementRow> Paid);

/// <summary>Demonstrativos em aberto; <paramref name="Reported"/> deles já têm pagamento informado.</summary>
public sealed record StudentOpenSummary(int Count, decimal Total, int Reported);

/// <summary>Demonstrativos do aluno logado em todos os vínculos dele (um aluno pode ter vários personais, D-015).</summary>
public sealed class StudentPaymentsQuery(AppDbContext db, TimeProvider clock)
{
    /// <summary>O histórico mostra os mais recentes.</summary>
    public const int PaidLimit = 36;

    public async Task<StudentPaymentsView> GetAsync(Guid studentUserId, CancellationToken cancellationToken)
    {
        var open = (await Statements(studentUserId, paid: false).ToListAsync(cancellationToken))
            .OrderBy(r => r.DueDate).ThenBy(r => r.PersonalName)
            .ToList();
        var paid = (await Statements(studentUserId, paid: true, take: PaidLimit).ToListAsync(cancellationToken))
            .OrderByDescending(r => r.ReferenceMonth).ThenBy(r => r.PersonalName)
            .ToList();

        var links = await Links(studentUserId)
            .Select(x => new { x.Link.Id, x.Link.BillingDueDay, x.Link.Status, x.PersonalName })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var previews = await db.NextClosingPreviewsAsync(
            links.Select(l => new PreviewLink(l.Id, l.BillingDueDay)).ToList(), nowUtc, cancellationToken);

        var previewRows = links
            .Where(l => l.Status != PersonalStudentStatus.Inactive || previews[l.Id].LessonCount > 0)
            .Select(l => new StudentPreviewRow(l.PersonalName, previews[l.Id].ReferenceMonth, previews[l.Id].ClosingDate,
                previews[l.Id].LessonCount, previews[l.Id].Total))
            .OrderBy(p => p.ClosingDate).ThenBy(p => p.PersonalName)
            .ToList();

        return new StudentPaymentsView(open, previewRows, paid);
    }

    public async Task<StudentOpenSummary> OpenSummaryAsync(Guid studentUserId, CancellationToken cancellationToken)
    {
        var open = await db.BillingPeriods
            .Where(p => p.Status != BillingPeriodStatus.Paid)
            .Join(Links(studentUserId), p => p.PersonalStudentId, l => l.Link.Id, (p, l) => new { p.TotalAmount, p.Status })
            .ToListAsync(cancellationToken);
        return new StudentOpenSummary(open.Count, open.Sum(p => p.TotalAmount),
            open.Count(p => p.Status == BillingPeriodStatus.PaymentReported));
    }

    private IQueryable<LinkRow> Links(Guid studentUserId) =>
        db.PersonalStudents
            .Where(s => s.StudentUserId == studentUserId)
            .Join(db.PersonalProfiles, s => s.PersonalId, p => p.Id, (s, p) => new LinkRow { Link = s, PersonalName = p.DisplayName });

    /// <summary>Filtra e ordena antes de projetar no record (o EF não traduz filtros sobre o construtor).</summary>
    private IQueryable<StudentStatementRow> Statements(Guid studentUserId, bool paid, int take = int.MaxValue) =>
        db.BillingPeriods
            .Where(p => paid ? p.Status == BillingPeriodStatus.Paid : p.Status != BillingPeriodStatus.Paid)
            .Where(p => Links(studentUserId).Any(l => l.Link.Id == p.PersonalStudentId))
            .OrderByDescending(p => p.ReferenceMonth)
            .Take(take)
            .Join(Links(studentUserId), p => p.PersonalStudentId, l => l.Link.Id, (p, l) => new StudentStatementRow(
                p.Id, l.PersonalName, p.ReferenceMonth, p.DueDate,
                db.BillingPeriodItems.Count(i => i.BillingPeriodId == p.Id && i.Kind == BillingPeriodItemKind.Lesson),
                p.TotalAmount, p.Status))
            .AsNoTracking();

    private sealed class LinkRow
    {
        public required PersonalStudent Link { get; init; }
        public required string PersonalName { get; init; }
    }
}
