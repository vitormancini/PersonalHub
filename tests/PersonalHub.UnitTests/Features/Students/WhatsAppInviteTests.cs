using PersonalHub.Web.Features.Students.CreateStudent;

namespace PersonalHub.UnitTests.Features.Students;

public class WhatsAppInviteTests
{
    [Theory]
    [InlineData("Mariana Lopes", "Mariana")]
    [InlineData("  Ana  ", "Ana")]
    [InlineData("João", "João")]
    public void FirstName_TakesTheFirstWord(string name, string expected)
    {
        Assert.Equal(expected, WhatsAppInvite.FirstName(name));
    }

    [Fact]
    public void Message_FollowsTheApprovedText()
    {
        var message = WhatsAppInvite.Message("Mariana Lopes", "Ana Souza", "https://app.example/convite/abc");

        Assert.Equal(
            "Olá, Mariana! Ana Souza convidou você para o PersonalHub. Crie sua senha por este link (vale por 7 dias): https://app.example/convite/abc",
            message);
    }

    [Fact]
    public void Url_UsesDigitsOnlyAndEncodesTheWholeMessage()
    {
        var url = WhatsAppInvite.Url("+5511999998888", "Olá, Ana & Cia! https://app.example/convite/a_b-c?x=1#y");

        Assert.Equal(
            "https://wa.me/5511999998888?text=Ol%C3%A1%2C%20Ana%20%26%20Cia%21%20https%3A%2F%2Fapp.example%2Fconvite%2Fa_b-c%3Fx%3D1%23y",
            url);
        var text = Uri.UnescapeDataString(new Uri(url).Query["?text=".Length..]);
        Assert.Equal("Olá, Ana & Cia! https://app.example/convite/a_b-c?x=1#y", text);
    }
}
