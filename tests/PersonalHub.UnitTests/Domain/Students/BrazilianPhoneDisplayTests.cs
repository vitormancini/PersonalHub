using PersonalHub.Web.Domain.Students;

namespace PersonalHub.UnitTests.Domain.Students;

public class BrazilianPhoneDisplayTests
{
    [Theory]
    [InlineData("+5511987654321", "(11) 98765-4321")]
    [InlineData("+551123456789", "(11) 2345-6789")]
    [InlineData("+14155550100", "+14155550100")]
    public void ToDisplay_FormatsBrazilianNumbers(string e164, string expected) =>
        Assert.Equal(expected, BrazilianPhone.ToDisplay(e164));

    [Theory]
    [InlineData("(11) 98765-4321")]
    [InlineData("(11) 2345-6789")]
    public void ToDisplay_RoundTripsThroughToE164(string display) =>
        Assert.Equal(display, BrazilianPhone.ToDisplay(BrazilianPhone.ToE164(display)!));
}
