using PersonalHub.Web.Features.Students;

namespace PersonalHub.UnitTests.Features.Students;

public class StudentDisplayTests
{
    [Theory]
    [InlineData("80", "R$ 80,00")]
    [InlineData("1234.5", "R$ 1.234,50")]
    [InlineData("0.01", "R$ 0,01")]
    public void Money_UsesBrazilianFormatWithPlainSpace(string value, string expected) =>
        Assert.Equal(expected, StudentDisplay.Money(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("Mariana Lopes", "ML")]
    [InlineData("ana maria de souza", "AS")]
    [InlineData("Ícaro", "Í")]
    [InlineData("  ", "?")]
    public void Initials_FirstAndLastName(string name, string expected) =>
        Assert.Equal(expected, StudentDisplay.Initials(name));

    [Fact]
    public void Date_UsesSaoPauloLocalDate() =>
        // 02:00 UTC ainda é o dia anterior em Brasília.
        Assert.Equal("04/11/2026", StudentDisplay.Date(new DateTime(2026, 11, 5, 2, 0, 0, DateTimeKind.Utc)));
}
