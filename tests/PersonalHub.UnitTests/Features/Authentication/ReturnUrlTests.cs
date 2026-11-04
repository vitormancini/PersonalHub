using PersonalHub.Web.Features.Authentication.Login;

namespace PersonalHub.UnitTests.Features.Authentication;

public class ReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/personal")]
    [InlineData("/aluno?mes=2027-01")]
    [InlineData("/personal#topo")]
    public void IsLocal_AcceptsLocalPaths(string url)
    {
        Assert.True(ReturnUrl.IsLocal(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("personal")]
    [InlineData("https://malicioso.example")]
    [InlineData("//malicioso.example")]
    [InlineData("/\\malicioso.example")]
    [InlineData("/\t/malicioso.example")]
    [InlineData("/\r\n/malicioso.example")]
    [InlineData("~/personal")]
    [InlineData("javascript:alert(1)")]
    public void IsLocal_RejectsExternalOrAmbiguousUrls(string? url)
    {
        Assert.False(ReturnUrl.IsLocal(url));
    }
}
