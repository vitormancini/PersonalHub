namespace PersonalHub.Web.Domain.Billing;

/// <summary>
/// Pagamento de um demonstrativo, sempre do valor total (D-024). Nasce <see cref="PaymentStatus.Reported"/> quando o
/// aluno informa (D-049) e o personal confirma (D-050) ou recusa (D-051). O registro direto pelo personal (D-052) já
/// nasce <see cref="PaymentStatus.Confirmed"/>, com o personal como quem informou e quem decidiu.
/// </summary>
public class Payment
{
    /// <summary>Limite da observação do aluno e da mensagem do personal ao recusar.</summary>
    public const int TextMaxLength = 500;

    public Guid Id { get; set; }
    public Guid BillingPeriodId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public Guid ReportedByUserId { get; set; }
    public DateTime ReportedAtUtc { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>Observação opcional do aluno ao informar.</summary>
    public string? Notes { get; set; }

    /// <summary>Mensagem opcional do personal ao recusar ("Não recebi"), visível ao aluno (D-051).</summary>
    public string? DecisionNote { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Registrado direto pelo personal, sem aviso do aluno (D-052): quem informou é quem decidiu.</summary>
    public bool RecordedByPersonal => DecidedByUserId == ReportedByUserId;

    /// <summary>O aluno informa o pagamento de um demonstrativo aguardando pagamento (D-049).</summary>
    public static Payment Report(BillingPeriod period, Guid studentUserId, string? notes, DateTime nowUtc)
    {
        period.MarkPaymentReported();
        return new Payment
        {
            Id = Guid.NewGuid(),
            BillingPeriodId = period.Id,
            Amount = period.TotalAmount,
            Status = PaymentStatus.Reported,
            ReportedByUserId = studentUserId,
            ReportedAtUtc = nowUtc,
            Notes = notes,
            CreatedAtUtc = nowUtc
        };
    }

    /// <summary>O personal registra o recebimento sem aviso do aluno (D-052).</summary>
    public static Payment RecordByPersonal(BillingPeriod period, Guid personalUserId, DateTime nowUtc)
    {
        if (!period.AwaitsPayment)
        {
            // Com pagamento informado, o caminho é confirmar o aviso do aluno.
            throw new InvalidOperationException("Só um demonstrativo aguardando pagamento pode ter o recebimento registrado.");
        }

        period.MarkPaid(nowUtc);
        return new Payment
        {
            Id = Guid.NewGuid(),
            BillingPeriodId = period.Id,
            Amount = period.TotalAmount,
            Status = PaymentStatus.Confirmed,
            ReportedByUserId = personalUserId,
            ReportedAtUtc = nowUtc,
            DecidedByUserId = personalUserId,
            DecidedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc
        };
    }

    /// <summary>O personal confirma o recebimento: pagamento e demonstrativo mudam juntos (D-050).</summary>
    public void Confirm(BillingPeriod period, Guid personalUserId, DateTime nowUtc)
    {
        EnsureReportedFor(period);
        period.MarkPaid(nowUtc);
        Status = PaymentStatus.Confirmed;
        DecidedByUserId = personalUserId;
        DecidedAtUtc = nowUtc;
    }

    /// <summary>"Não recebi": o demonstrativo volta a aguardar pagamento e o aluno pode informar de novo (D-051).</summary>
    public void Reject(BillingPeriod period, Guid personalUserId, string? message, DateTime nowUtc)
    {
        EnsureReportedFor(period);
        period.ReopenAfterRejection();
        Status = PaymentStatus.Rejected;
        DecidedByUserId = personalUserId;
        DecidedAtUtc = nowUtc;
        DecisionNote = message;
    }

    private void EnsureReportedFor(BillingPeriod period)
    {
        if (Status != PaymentStatus.Reported || BillingPeriodId != period.Id)
        {
            throw new InvalidOperationException("Só um pagamento informado deste demonstrativo pode ser decidido.");
        }
    }
}
