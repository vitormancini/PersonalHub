using PersonalHub.Web.Domain.Billing;

namespace PersonalHub.UnitTests.Domain.Billing;

/// <summary>Transições do pagamento e do demonstrativo (D-024, D-049 a D-053).</summary>
public class PaymentTests
{
    private static readonly Guid Student = Guid.NewGuid();
    private static readonly Guid Personal = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

    private static BillingPeriod Period(BillingPeriodStatus status = BillingPeriodStatus.Issued, decimal total = 160m) => new()
    {
        Id = Guid.NewGuid(),
        PersonalStudentId = Guid.NewGuid(),
        ReferenceMonth = new DateOnly(2026, 10, 1),
        TotalAmount = total,
        Status = status,
        PaidAtUtc = status == BillingPeriodStatus.Paid ? Now.AddDays(-1) : null
    };

    [Fact]
    public void Report_CreatesReportedPaymentOfTheTotal_AndMovesPeriodToPaymentReported()
    {
        var period = Period();

        var payment = Payment.Report(period, Student, "Paguei por Pix", Now);

        Assert.Equal(PaymentStatus.Reported, payment.Status);
        Assert.Equal(160m, payment.Amount);
        Assert.Equal(period.Id, payment.BillingPeriodId);
        Assert.Equal(Student, payment.ReportedByUserId);
        Assert.Equal(Now, payment.ReportedAtUtc);
        Assert.Equal("Paguei por Pix", payment.Notes);
        Assert.Null(payment.DecidedAtUtc);
        Assert.False(payment.RecordedByPersonal);
        Assert.Equal(BillingPeriodStatus.PaymentReported, period.Status);
        Assert.Null(period.PaidAtUtc);
    }

    [Theory]
    [InlineData(BillingPeriodStatus.PaymentReported)]
    [InlineData(BillingPeriodStatus.Paid)]
    public void Report_OnlyFromIssued(BillingPeriodStatus status)
    {
        var period = Period(status);

        Assert.Throws<InvalidOperationException>(() => Payment.Report(period, Student, null, Now));
        Assert.Equal(status, period.Status);
    }

    [Fact]
    public void Report_ZeroTotal_IsAlreadyPaidAndCannotBeReported()
    {
        Assert.Throws<InvalidOperationException>(() => Payment.Report(Period(BillingPeriodStatus.Paid, total: 0m), Student, null, Now));
    }

    [Fact]
    public void Confirm_MarksPaymentConfirmedAndPeriodPaidTogether()
    {
        var period = Period();
        var payment = Payment.Report(period, Student, null, Now);

        payment.Confirm(period, Personal, Now.AddHours(2));

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(Personal, payment.DecidedByUserId);
        Assert.Equal(Now.AddHours(2), payment.DecidedAtUtc);
        Assert.Equal(BillingPeriodStatus.Paid, period.Status);
        Assert.Equal(Now.AddHours(2), period.PaidAtUtc);
    }

    [Fact]
    public void Reject_ReopensPeriod_KeepsMessage_AndAllowsReportingAgain()
    {
        var period = Period();
        var first = Payment.Report(period, Student, null, Now);

        first.Reject(period, Personal, "Não caiu ainda", Now.AddHours(1));

        Assert.Equal(PaymentStatus.Rejected, first.Status);
        Assert.Equal("Não caiu ainda", first.DecisionNote);
        Assert.Equal(Now.AddHours(1), first.DecidedAtUtc);
        Assert.Equal(BillingPeriodStatus.Issued, period.Status);
        Assert.Null(period.PaidAtUtc);

        var second = Payment.Report(period, Student, null, Now.AddHours(3));
        Assert.Equal(PaymentStatus.Reported, second.Status);
        Assert.Equal(BillingPeriodStatus.PaymentReported, period.Status);
    }

    [Fact]
    public void Decisions_AreFinal_ConfirmedCannotBeRejectedOrConfirmedAgain()
    {
        var period = Period();
        var payment = Payment.Report(period, Student, null, Now);
        payment.Confirm(period, Personal, Now);

        Assert.Throws<InvalidOperationException>(() => payment.Reject(period, Personal, null, Now));
        Assert.Throws<InvalidOperationException>(() => payment.Confirm(period, Personal, Now));
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(BillingPeriodStatus.Paid, period.Status);
    }

    [Fact]
    public void Rejected_CannotBeConfirmedLater()
    {
        var period = Period();
        var payment = Payment.Report(period, Student, null, Now);
        payment.Reject(period, Personal, null, Now);

        Assert.Throws<InvalidOperationException>(() => payment.Confirm(period, Personal, Now));
        Assert.Equal(BillingPeriodStatus.Issued, period.Status);
    }

    [Fact]
    public void Decide_PaymentOfAnotherPeriod_Throws()
    {
        var period = Period();
        var payment = Payment.Report(period, Student, null, Now);
        var other = Period(BillingPeriodStatus.PaymentReported);

        Assert.Throws<InvalidOperationException>(() => payment.Confirm(other, Personal, Now));
        Assert.Equal(BillingPeriodStatus.PaymentReported, other.Status);
    }

    [Fact]
    public void RecordByPersonal_CreatesConfirmedPayment_AndPaysPeriod()
    {
        var period = Period();

        var payment = Payment.RecordByPersonal(period, Personal, Now);

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(160m, payment.Amount);
        Assert.Equal(Personal, payment.ReportedByUserId);
        Assert.Equal(Personal, payment.DecidedByUserId);
        Assert.True(payment.RecordedByPersonal);
        Assert.Null(payment.Notes);
        Assert.Equal(BillingPeriodStatus.Paid, period.Status);
        Assert.Equal(Now, period.PaidAtUtc);
    }

    [Theory]
    [InlineData(BillingPeriodStatus.PaymentReported)]
    [InlineData(BillingPeriodStatus.Paid)]
    public void RecordByPersonal_OnlyFromIssued(BillingPeriodStatus status)
    {
        var period = Period(status);

        Assert.Throws<InvalidOperationException>(() => Payment.RecordByPersonal(period, Personal, Now));
        Assert.Equal(status, period.Status);
    }

    [Theory]
    [InlineData(BillingPeriodStatus.Issued, true, false)]
    [InlineData(BillingPeriodStatus.PaymentReported, false, true)]
    [InlineData(BillingPeriodStatus.Paid, false, false)]
    public void PeriodFlags(BillingPeriodStatus status, bool awaitsPayment, bool awaitsConfirmation)
    {
        var period = Period(status);

        Assert.Equal(awaitsPayment, period.AwaitsPayment);
        Assert.Equal(awaitsConfirmation, period.AwaitsConfirmation);
    }
}
