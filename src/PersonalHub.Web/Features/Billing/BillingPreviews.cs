using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Billing;

/// <summary>Vínculo para o qual se quer a prévia do próximo fechamento.</summary>
public sealed record PreviewLink(Guid Id, short BillingDueDay);

/// <summary>Prévia do próximo demonstrativo de um vínculo, com as aulas confirmadas até agora.</summary>
public sealed record BillingPreview(Guid PersonalStudentId, DateOnly ReferenceMonth, DateOnly ClosingDate, int LessonCount, decimal Total);

public static class BillingPreviews
{
    /// <summary>
    /// Calcula, com as mesmas regras do fechamento (<see cref="BillingClosing.Preview"/>), quanto sairia o próximo
    /// demonstrativo de cada vínculo se fechasse agora.
    /// </summary>
    public static async Task<Dictionary<Guid, BillingPreview>> NextClosingPreviewsAsync(
        this AppDbContext db, IReadOnlyCollection<PreviewLink> links, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (links.Count == 0)
        {
            return [];
        }

        var today = SaoPaulo.ToLocalDate(nowUtc);
        var ids = links.Select(l => l.Id).ToList();

        // O próximo fechamento é do mês passado, do atual ou (com o dia trocado) do seguinte: busca até o fim do
        // mês seguinte; o domínio filtra pelo mês de referência.
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        var (_, monthEndUtc) = SaoPaulo.MonthRangeUtc(nextMonth.Year, nextMonth.Month);
        var lessons = await db.ClosingLessonsAsync(ids, monthEndUtc, cancellationToken);
        var carried = await db.CarriedCreditsAsync(ids, cancellationToken);
        var latest = await db.BillingPeriods
            .Where(p => ids.Contains(p.PersonalStudentId))
            .GroupBy(p => p.PersonalStudentId)
            .Select(g => new { PersonalStudentId = g.Key, ReferenceMonth = g.Max(p => p.ReferenceMonth) })
            .ToDictionaryAsync(x => x.PersonalStudentId, x => (DateOnly?)x.ReferenceMonth, cancellationToken);

        return links.ToDictionary(l => l.Id, l =>
        {
            var (reference, closingDate) = BillingClosing.NextClosing(today, l.BillingDueDay,
                latest.GetValueOrDefault(l.Id));
            var result = BillingClosing.Preview(reference, l.BillingDueDay, lessons[l.Id], carried.GetValueOrDefault(l.Id), nowUtc);
            return new BillingPreview(l.Id, reference, closingDate, result?.LessonCount ?? 0, result?.TotalAmount ?? 0m);
        });
    }
}
