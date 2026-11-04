using PersonalHub.Web.Domain.Students;

namespace PersonalHub.UnitTests.Domain.Students;

public class LessonPriceTests
{
    [Theory]
    [InlineData("80", "80")]
    [InlineData("80,00", "80.00")]
    [InlineData("80,5", "80.5")]
    [InlineData("R$ 80,00", "80.00")]
    [InlineData("r$120", "120")]
    [InlineData("1.234,56", "1234.56")]
    [InlineData("1234,56", "1234.56")]
    [InlineData("1.500", "1500")]
    [InlineData("99.999,99", "99999.99")]
    [InlineData("80.50", "80.50")]   // teclado em inglês
    [InlineData(" 0,01 ", "0.01")]
    [InlineData("80.123", "80123")]  // ponto seguido de 3 dígitos é milhar, nunca 80,123
    public void TryParse_ReadsBrazilianFormat(string input, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), LessonPrice.TryParse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("80,")]
    [InlineData("80,001")]
    [InlineData("1.23,45")]
    [InlineData("12.34.56")]
    [InlineData("-10")]
    [InlineData("10,5,0")]
    [InlineData("1e3")]
    [InlineData("1234567890,00")]
    public void TryParse_RejectsInvalidText(string? input)
    {
        Assert.Null(LessonPrice.TryParse(input));
    }

    [Theory]
    [InlineData("0.01", true)]
    [InlineData("80", true)]
    [InlineData("99999.99", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("100000", false)]
    [InlineData("10.001", false)]
    public void IsAllowed_RequiresPositiveUpToMaxWithTwoDecimals(string value, bool expected)
    {
        Assert.Equal(expected, LessonPrice.IsAllowed(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
