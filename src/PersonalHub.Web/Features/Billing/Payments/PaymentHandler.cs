using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Billing.Payments;

public enum PaymentOutcome
{
    Succeeded,

    /// <summary>Demonstrativo de outro usuário ou inexistente.</summary>
    NotFound,

    /// <summary>Já há pagamento informado aguardando o personal (duplo toque, outra aba, ou o aluno avisou antes).</summary>
    AlreadyReported,

    /// <summary>O demonstrativo já está pago. A confirmação é definitiva (D-053).</summary>
    AlreadyPaid,

    /// <summary>Não há pagamento informado para confirmar ou recusar (já recusado em outra aba).</summary>
    NotReported,

    TextTooLong
}

/// <summary>
/// Transições do pagamento (D-049 a D-053). Cada operação trava o demonstrativo com <c>FOR UPDATE</c> e confere a
/// situação gravada; a regra das transições fica no domínio (<see cref="Payment"/>, <see cref="BillingPeriod"/>).
/// Pagamento e demonstrativo mudam na mesma transação.
/// </summary>
public sealed partial class PaymentHandler(AppDbContext db, TimeProvider clock, ILogger<PaymentHandler> logger)
{
    /// <summary>O aluno informa que pagou o valor total (D-049).</summary>
    public async Task<PaymentOutcome> ReportAsync(
        Guid studentUserId, Guid billingPeriodId, string? notes, string? correlationId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var period = await db.LockPeriodForStudentAsync(studentUserId, billingPeriodId, cancellationToken);
        if (StateOutcome(period, expect: BillingPeriodStatus.Issued) is { } blocked)
        {
            return blocked;
        }

        var text = LessonRules.NormalizeText(notes);
        if (text is { Length: > Payment.TextMaxLength })
        {
            return PaymentOutcome.TextTooLong;
        }

        var now = Now;
        var payment = Payment.Report(period!, studentUserId, text, now);
        db.Payments.Add(payment);
        db.AddPaymentAudit(studentUserId, "PaymentReported", payment, now, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogChanged(logger, payment.Id, billingPeriodId, studentUserId, payment.Status);
        return PaymentOutcome.Succeeded;
    }

    /// <summary>O personal confirma o recebimento do pagamento informado (D-050).</summary>
    public Task<PaymentOutcome> ConfirmAsync(
        Guid personalUserId, Guid billingPeriodId, string? correlationId, CancellationToken cancellationToken) =>
        DecideAsync(personalUserId, billingPeriodId, confirm: true, null, correlationId, cancellationToken);

    /// <summary>"Não recebi", com mensagem opcional para o aluno (D-051).</summary>
    public Task<PaymentOutcome> RejectAsync(
        Guid personalUserId, Guid billingPeriodId, string? message, string? correlationId, CancellationToken cancellationToken) =>
        DecideAsync(personalUserId, billingPeriodId, confirm: false, message, correlationId, cancellationToken);

    /// <summary>O personal registra o recebimento sem aviso do aluno (D-052).</summary>
    public async Task<PaymentOutcome> RecordAsync(
        Guid personalUserId, Guid billingPeriodId, string? correlationId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var period = await db.LockPeriodForPersonalAsync(personalUserId, billingPeriodId, cancellationToken);
        if (StateOutcome(period, expect: BillingPeriodStatus.Issued) is { } blocked)
        {
            return blocked;
        }

        var now = Now;
        var payment = Payment.RecordByPersonal(period!, personalUserId, now);
        db.Payments.Add(payment);
        db.AddPaymentAudit(personalUserId, "PaymentRecorded", payment, now, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogChanged(logger, payment.Id, billingPeriodId, personalUserId, payment.Status);
        return PaymentOutcome.Succeeded;
    }

    private async Task<PaymentOutcome> DecideAsync(
        Guid personalUserId, Guid billingPeriodId, bool confirm, string? message, string? correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var period = await db.LockPeriodForPersonalAsync(personalUserId, billingPeriodId, cancellationToken);
        if (StateOutcome(period, expect: BillingPeriodStatus.PaymentReported) is { } blocked)
        {
            return blocked;
        }

        var payment = await db.Payments
            .SingleOrDefaultAsync(p => p.BillingPeriodId == billingPeriodId && p.Status == PaymentStatus.Reported, cancellationToken);
        if (payment is null)
        {
            return PaymentOutcome.NotReported;
        }

        var now = Now;
        if (confirm)
        {
            payment.Confirm(period!, personalUserId, now);
        }
        else
        {
            var text = LessonRules.NormalizeText(message);
            if (text is { Length: > Payment.TextMaxLength })
            {
                return PaymentOutcome.TextTooLong;
            }

            payment.Reject(period!, personalUserId, text, now);
        }

        db.AddPaymentAudit(personalUserId, confirm ? "PaymentConfirmed" : "PaymentRejected", payment, now, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogChanged(logger, payment.Id, billingPeriodId, personalUserId, payment.Status);
        return PaymentOutcome.Succeeded;
    }

    /// <summary>Nulo quando o demonstrativo está na situação esperada; senão, o motivo de não seguir.</summary>
    private static PaymentOutcome? StateOutcome(BillingPeriod? period, BillingPeriodStatus expect) => period switch
    {
        null => PaymentOutcome.NotFound,
        { Status: var status } when status == expect => null,
        { Status: BillingPeriodStatus.Paid } => PaymentOutcome.AlreadyPaid,
        { Status: BillingPeriodStatus.PaymentReported } => PaymentOutcome.AlreadyReported,
        _ => PaymentOutcome.NotReported
    };

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [LoggerMessage(EventId = 6000, Level = LogLevel.Information,
        Message = "Pagamento {PaymentId} do demonstrativo {BillingPeriodId} alterado pelo usuário {UserId}: {Status}")]
    private static partial void LogChanged(ILogger logger, Guid paymentId, Guid billingPeriodId, Guid userId, PaymentStatus status);
}
