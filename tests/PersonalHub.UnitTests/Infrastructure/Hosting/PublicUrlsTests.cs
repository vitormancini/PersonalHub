using Microsoft.Extensions.Options;
using PersonalHub.Web.Infrastructure.Hosting;

namespace PersonalHub.UnitTests.Infrastructure.Hosting;

public class PublicUrlsTests
{
    [Theory]
    [InlineData("https://app.exemplo.com")]
    [InlineData("https://app.exemplo.com/")]
    [InlineData("https://app.exemplo.com:8443")]
    public void TryParse_AcceptsHttpsRoot(string value)
    {
        Assert.NotNull(PublicUrls.TryParse(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("app.exemplo.com")]
    [InlineData("/convite")]
    [InlineData("http://app.exemplo.com")]
    [InlineData("https://app.exemplo.com/sub")]
    [InlineData("https://app.exemplo.com/?a=1")]
    [InlineData("https://app.exemplo.com/#x")]
    [InlineData("https://user:senha@app.exemplo.com")]
    public void TryParse_RejectsAnythingElse(string? value)
    {
        Assert.Null(PublicUrls.TryParse(value));
    }

    [Fact]
    public void Absolute_UsesConfiguredBase_InsteadOfRequest()
    {
        var urls = Create("https://app.exemplo.com");

        Assert.Equal("https://app.exemplo.com/convite/abc", urls.Absolute("/convite/abc", "https://atacante.example/"));
    }

    [Fact]
    public void Absolute_FallsBackToRequest_WhenNotConfigured()
    {
        var urls = Create(null);

        Assert.Equal("https://localhost:5001/convite/abc", urls.Absolute("/convite/abc", "https://localhost:5001/"));
    }

    private static PublicUrls Create(string? baseUrl) =>
        new(Options.Create(new PublicUrlOptions { PublicBaseUrl = baseUrl }));
}
