using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Students;

/// <summary>O link do convite usa App:PublicBaseUrl, nunca o header Host enviado pelo cliente.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class InviteLinkTests(DatabaseFixture fixture)
{
    private const string PublicBaseUrl = "https://app.personalhub.test";
    private const string SpoofedHost = "atacante.example";

    [Fact]
    public async Task InviteLink_UsesConfiguredPublicBaseUrl_AndIgnoresSpoofedHost()
    {
        await using var factory = TestApp.Create(fixture.ConnectionString, new Dictionary<string, string?>
        {
            [TestApp.PublicBaseUrlKey] = PublicBaseUrl
        });
        var client = factory.CreateBrowser();

        // Controle: o servidor de teste repassa o Host forjado (a raiz redireciona com URL absoluta do Blazor).
        var control = await client.SendAsync(WithHost(HttpMethod.Get, "/"));
        Assert.Equal(SpoofedHost, control.Headers.Location?.Host);

        var personal = await factory.CreateAccountAsync(AppRoles.Personal);
        await client.PostLoginAsync(personal.Email, personal.Password);
        var created = await client.PostCreateStudentAsync(StudentTestHelpers.ValidStudentFields());

        var page = await client.SendAsync(WithHost(HttpMethod.Get, created.RedirectTarget()));
        var html = await page.TextAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Matches($"{Regex.Escape(PublicBaseUrl)}/convite/[A-Za-z0-9_-]{{43}}", html);
        Assert.DoesNotContain(SpoofedHost, html);
        Assert.DoesNotContain(Uri.EscapeDataString(SpoofedHost), html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://app.personalhub.test")]
    [InlineData("https://app.personalhub.test/sub")]
    [InlineData("app.personalhub.test")]
    public async Task Startup_FailsOutsideDevelopment_WithoutValidPublicBaseUrl(string value)
    {
        await using var factory = CreateFactory("Production", value);

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(TestApp.PublicBaseUrlKey, Flatten(error));
    }

    [Theory]
    [InlineData("Production", PublicBaseUrl)]
    [InlineData("Development", "")]
    public async Task Startup_Succeeds_WhenPublicBaseUrlIsValidOrDevelopment(string environment, string value)
    {
        await using var factory = CreateFactory(environment, value);

        var response = await factory.CreateBrowser().GetAsync("/entrar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory(string environment, string publicBaseUrl) =>
        TestApp.Create(fixture.ConnectionString, new Dictionary<string, string?>
            {
                [TestApp.PublicBaseUrlKey] = publicBaseUrl,
                ["AllowedHosts"] = "localhost"
            })
            .WithWebHostBuilder(builder => builder.UseEnvironment(environment));

    private static HttpRequestMessage WithHost(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Host = SpoofedHost;
        return request;
    }

    private static string Flatten(Exception error) =>
        error is AggregateException aggregate
            ? string.Join(" | ", aggregate.Flatten().InnerExceptions.Select(Flatten))
            : error.Message + (error.InnerException is null ? "" : " | " + Flatten(error.InnerException));
}
