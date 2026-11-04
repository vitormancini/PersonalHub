namespace PersonalHub.Web.Domain.Billing;

/// <summary>Aviso de pagamento do aluno, confirmado ou recusado pelo personal (D-024).</summary>
public class Payment
{
    public Guid Id { get; set; }
    public Guid BillingPeriodId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public Guid ReportedByUserId { get; set; }
    public DateTime ReportedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
