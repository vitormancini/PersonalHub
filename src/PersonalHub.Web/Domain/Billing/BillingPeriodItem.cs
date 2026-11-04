namespace PersonalHub.Web.Domain.Billing;

/// <summary>Linha do demonstrativo: cobrança de aula (valor positivo) ou crédito (valor negativo).</summary>
public class BillingPeriodItem
{
    public Guid Id { get; set; }
    public Guid BillingPeriodId { get; set; }
    public BillingPeriodItemKind Kind { get; set; }
    public Guid? LessonId { get; set; }

    /// <summary>Texto congelado para o demonstrativo.</summary>
    public required string Description { get; set; }

    public decimal Amount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
