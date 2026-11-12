using PersonalHub.Web.Domain.Notifications;

namespace PersonalHub.UnitTests.Domain.Notifications;

public class NotificationTemplatesTests
{
    [Theory]
    [InlineData("Mariana\nLopes", "Mariana Lopes")]
    [InlineData("  Carlos\r\n\tLima  ", "Carlos Lima")]
    [InlineData("Ana     Paula", "Ana Paula")]
    [InlineData("", "-")]
    [InlineData(null, "-")]
    [InlineData("\n\n", "-")]
    public void Variable_HasNoLineBreaksTabsOrRepeatedSpaces(string? input, string expected)
    {
        Assert.Equal(expected, NotificationTemplates.Variable(input));
    }

    [Fact]
    public void Variable_IsLimitedInLength()
    {
        var text = NotificationTemplates.Variable(new string('a', 200));

        Assert.Equal(NotificationTemplates.VariableMaxLength, text.Length);
        Assert.EndsWith("…", text);
    }

    [Theory]
    [InlineData("Mariana Lopes", "Mariana")]
    [InlineData("  Paula\nReis", "Paula")]
    [InlineData("Carlos", "Carlos")]
    public void FirstName_TakesTheFirstWord(string input, string expected)
    {
        Assert.Equal(expected, NotificationTemplates.FirstName(input));
    }
}
