using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Authentication;

[Collection(DatabaseCollection.Name)]
public sealed class AccessTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;

    public Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _client = _factory.CreateBrowser();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData("/personal")]
    [InlineData("/aluno")]
    public async Task ProtectedPage_RedirectsAnonymousToLogin(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("/entrar", location.AbsolutePath);
        Assert.Contains($"ReturnUrl={Uri.EscapeDataString(path)}", location.Query);
    }

    [Fact]
    public async Task Root_RedirectsAnonymousToLogin()
    {
        var response = await _client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/entrar", response.RedirectTarget());
    }

    [Fact]
    public async Task Root_RedirectsAuthenticatedUserToHome()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _client.PostLoginAsync(account.Email, account.Password);

        var response = await _client.GetAsync("/");

        Assert.Equal("/personal", response.RedirectTarget());
    }

    [Fact]
    public async Task Student_CannotOpenPersonalArea_IsSentToOwnHome()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Student);
        await _client.PostLoginAsync(account.Email, account.Password);

        var response = await _client.GetAsync("/personal");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/aluno", response.RedirectTarget());
    }

    [Fact]
    public async Task Personal_CannotOpenStudentArea_IsSentToOwnHome()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _client.PostLoginAsync(account.Email, account.Password);

        var response = await _client.GetAsync("/aluno");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/personal", response.RedirectTarget());
    }

    [Fact]
    public async Task Logout_WithoutAntiforgeryToken_IsRejected_AndKeepsSession()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _client.PostLoginAsync(account.Email, account.Password);

        var response = await _client.PostAsync("/sair", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/personal")).StatusCode);
    }

    [Fact]
    public async Task Logout_WithAntiforgeryToken_ClearsCookie_AndRedirectsToLogin()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _client.PostLoginAsync(account.Email, account.Password);
        var home = await _client.GetAsync("/personal");
        var token = AuthTestHelpers.AntiforgeryToken(await home.Content.ReadAsStringAsync());

        var response = await _client.PostAsync("/sair", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/entrar", response.RedirectTarget());
        var cleared = AuthTestHelpers.AuthCookieHeader(response);
        Assert.NotNull(cleared);
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);

        var afterLogout = await _client.GetAsync("/personal");
        Assert.StartsWith("/entrar?ReturnUrl=", afterLogout.RedirectTarget());
    }

    [Fact]
    public async Task Logout_DoesNotAcceptGet()
    {
        // Logado: anônimo cairia na política padrão (login) antes de chegar ao 405.
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _client.PostLoginAsync(account.Email, account.Password);

        var response = await _client.GetAsync("/sair");

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(AuthTestHelpers.AuthCookieHeader(response));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/personal")).StatusCode);
    }

    [Fact]
    public async Task LoginPost_IsRateLimitedPerIp()
    {
        await using var factory = TestApp.Create(fixture.ConnectionString, new Dictionary<string, string?>
        {
            ["Authentication:LoginRateLimit:PermitLimit"] = "2"
        });
        var client = factory.CreateBrowser();

        // Os dois primeiros passam pelo limitador (e falham no antiforgery); o terceiro é barrado antes.
        var empty = new FormUrlEncodedContent([]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/entrar", empty)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/entrar", empty)).StatusCode);
        var limited = await client.PostAsync("/entrar", empty);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("Muitas tentativas", await limited.Content.ReadAsStringAsync());

        // GET da página continua liberado.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/entrar")).StatusCode);
    }
}
