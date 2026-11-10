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

        // O próximo fechamento é do mês passado ou do atual: basta buscar até o fim do mês atual; o domínio filtra.
        var (_, monthEndUtc) = SaoPaulo.MonthRangeUtc(today.Year, today.Month);
        var lessons = await db.ClosingLessonsAsync(ids, monthEndUtc, cancellationToken);
        var carried = await db.CarriedCreditsAsync(ids, cancellationToken);

        return links.ToDictionary(l => l.Id, l =>
        {
            var (reference, closingDate) = BillingClosing.NextClosing(today, l.BillingDueDay);
            var result = BillingClosing.Preview(reference, l.BillingDueDay, lessons[l.Id], carried.GetValueOrDefault(l.Id), nowUtc);
            return new BillingPreview(l.Id, reference, closingDate, result?.LessonCount ?? 0, result?.TotalAmount ?? 0m);
        });
    }
}
