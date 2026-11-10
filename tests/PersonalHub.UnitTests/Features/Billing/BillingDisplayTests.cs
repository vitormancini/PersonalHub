using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Features.Billing;

namespace PersonalHub.UnitTests.Features.Billing;

/// <summary>Rótulos do demonstrativo (D-048, D-049).</summary>
public class BillingDisplayTests
{
    [Theory]
    [InlineData(BillingPeriodStatus.Issued, "Aguardando pagamento", "ph-pill-pending")]
    [InlineData(BillingPeriodStatus.PaymentReported, "Pagamento informado", "ph-pill-info")]
    [InlineData(BillingPeriodStatus.Paid, "Pago", "ph-pill-success")]
    public void StatusLabelAndPill(BillingPeriodStatus status, string label, string pill)
    {
        Assert.Equal(label, BillingDisplay.StatusLabel(status));
        Assert.Equal(pill, BillingDisplay.StatusPillClass(status));
    }
}
