namespace PersonalHub.Web.Domain.Billing;

/// <summary>
/// Demonstrativo mensal de um vínculo. Depois de emitido, itens e total nunca mudam (D-023); só a situação do
/// pagamento avança (D-049 a D-053).
/// </summary>
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

    /// <summary>O aluno pode informar o pagamento e o personal pode registrar o recebimento (D-049, D-052).</summary>
    public bool AwaitsPayment => Status == BillingPeriodStatus.Issued;

    /// <summary>Aguarda o personal confirmar ou recusar o pagamento informado.</summary>
    public bool AwaitsConfirmation => Status == BillingPeriodStatus.PaymentReported;

    internal void MarkPaymentReported()
    {
        if (!AwaitsPayment)
        {
            throw new InvalidOperationException("Só um demonstrativo aguardando pagamento pode ter pagamento informado.");
        }

        Status = BillingPeriodStatus.PaymentReported;
    }

    /// <summary>Confirmação é definitiva (D-053): não há volta de <see cref="BillingPeriodStatus.Paid"/>.</summary>
    internal void MarkPaid(DateTime nowUtc)
    {
        if (Status == BillingPeriodStatus.Paid)
        {
            throw new InvalidOperationException("O demonstrativo já está pago.");
        }

        Status = BillingPeriodStatus.Paid;
        PaidAtUtc = nowUtc;
    }

    internal void ReopenAfterRejection()
    {
        if (!AwaitsConfirmation)
        {
            throw new InvalidOperationException("Só um pagamento informado pode ser recusado.");
        }

        Status = BillingPeriodStatus.Issued;
    }
}
