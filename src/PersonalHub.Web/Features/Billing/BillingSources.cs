using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Billing;

/// <summary>
/// Consultas que alimentam o cálculo do fechamento (<see cref="BillingClosing"/>): usadas pelo job e pelas prévias,
/// para que a prévia mostre exatamente o que o fechamento faria.
/// </summary>
public static class BillingSources
{
    /// <summary>
    /// Aulas que podem entrar num fechamento dos vínculos informados: confirmadas, não excluídas e ainda não cobradas
    /// até <paramref name="monthEndUtc"/>; e excluídas já cobradas ainda não creditadas. O filtro fino (corte) fica
    /// no domínio.
    /// </summary>
    public static async Task<ILookup<Guid, ClosingLesson>> ClosingLessonsAsync(
        this AppDbContext db, IReadOnlyCollection<Guid> personalStudentIds, DateTime monthEndUtc,
        CancellationToken cancellationToken)
    {
        if (personalStudentIds.Count == 0)
        {
            return Array.Empty<(Guid, ClosingLesson)>().ToLookup(x => x.Item1, x => x.Item2);
        }

        var items = db.BillingPeriodItems;
        var rows = await db.Lessons
            .Where(l => personalStudentIds.Contains(l.PersonalStudentId))
            .Where(l =>
                (l.Status == LessonStatus.Approved && l.DeletedAtUtc == null && l.OccurredAtUtc < monthEndUtc
                 && !items.Any(i => i.LessonId == l.Id && i.Kind == BillingPeriodItemKind.Lesson))
                || (l.DeletedAtUtc != null
                    && items.Any(i => i.LessonId == l.Id && i.Kind == BillingPeriodItemKind.Lesson)
                    && !items.Any(i => i.LessonId == l.Id && i.Kind == BillingPeriodItemKind.Adjustment)))
            .Select(l => new
            {
                l.PersonalStudentId,
                Lesson = new ClosingLesson(
                    l.Id, l.OccurredAtUtc, l.UnitPrice, l.Status, l.RespondedAtUtc, l.DeletedAtUtc,
                    items.Where(i => i.LessonId == l.Id && i.Kind == BillingPeriodItemKind.Lesson)
                        .Select(i => (decimal?)i.Amount).FirstOrDefault(),
                    false)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.ToLookup(r => r.PersonalStudentId, r => r.Lesson);
    }

    /// <summary>Saldo de crédito que sobrou do demonstrativo mais recente de cada vínculo (D-044).</summary>
    public static async Task<Dictionary<Guid, CarriedCredit>> CarriedCreditsAsync(
        this AppDbContext db, IReadOnlyCollection<Guid> personalStudentIds, CancellationToken cancellationToken)
    {
        if (personalStudentIds.Count == 0)
        {
            return [];
        }

        var latest = await db.BillingPeriods
            .Where(p => personalStudentIds.Contains(p.PersonalStudentId))
            .Where(p => !db.BillingPeriods.Any(o => o.PersonalStudentId == p.PersonalStudentId && o.ReferenceMonth > p.ReferenceMonth))
            .Select(p => new
            {
                p.PersonalStudentId,
                p.ReferenceMonth,
                Sum = db.BillingPeriodItems.Where(i => i.BillingPeriodId == p.Id).Sum(i => i.Amount)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return latest
            .Where(p => p.Sum < 0)
            .ToDictionary(p => p.PersonalStudentId, p => new CarriedCredit(p.ReferenceMonth, BillingClosing.CarryOutOf(p.Sum)));
    }
}
