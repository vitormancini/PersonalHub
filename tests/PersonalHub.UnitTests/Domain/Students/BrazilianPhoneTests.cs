using PersonalHub.Web.Domain.Students;

namespace PersonalHub.UnitTests.Domain.Students;

public class BrazilianPhoneTests
{
    [Theory]
    [InlineData("(11) 99999-8888", "+5511999998888")]
    [InlineData("11999998888", "+5511999998888")]
    [InlineData("11 9 9999 8888", "+5511999998888")]
    [InlineData("+55 11 99999-8888", "+5511999998888")]
    [InlineData("+5511999998888", "+5511999998888")]
    [InlineData("5511999998888", "+5511999998888")]
    [InlineData("011 99999-8888", "+5511999998888")]
    [InlineData("(21) 3333-4444", "+552133334444")]
    [InlineData("  (85)98888.7777  ", "+5585988887777")]
    public void ToE164_NormalizesBrazilianFormats(string input, string expected)
    {
        Assert.Equal(expected, BrazilianPhone.ToE164(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("99999-8888")]          // sem DDD
    [InlineData("9999-8888")]           // sem DDD
    [InlineData("(01) 99999-8888")]     // DDD inválido
    [InlineData("(10) 99999-8888")]     // DDD inválido
    [InlineData("(11) 89999-8888")]     // 11 dígitos sem o 9 do celular
    [InlineData("(11) 1999-8888")]      // fixo começando por 1
    [InlineData("+1 415 555 0100")]     // fora do Brasil
    [InlineData("+55 11 99999-88889")]  // dígitos demais
    [InlineData("11 99999-8888 ramal 2")]
    [InlineData("+55+11999998888")]
    public void ToE164_RejectsInvalidNumbers(string? input)
    {
        Assert.Null(BrazilianPhone.ToE164(input));
    }
}
