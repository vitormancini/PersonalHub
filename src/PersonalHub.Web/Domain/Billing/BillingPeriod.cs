namespace PersonalHub.Web.Domain.Billing;

/// <summary>Demonstrativo mensal de um vínculo. Depois de emitido, nunca é alterado (D-023).</summary>
public class BillingPeriod
{
    public Guid Id { get; set; }
    public Guid PersonalStudentId { get; set; }

    /// <summary>Mês de referência, sempre no dia 1.</summary>
    public DateOnly ReferenceMonth { get; set; }

    /// <summary>Início inclusivo.</summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>Fim exclusivo.</summary>
    public DateOnly PeriodEnd { get; set; }

    public DateOnly DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public BillingPeriodStatus Status { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
