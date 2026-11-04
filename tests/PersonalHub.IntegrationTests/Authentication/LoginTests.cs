using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Observability;
using PersonalHub.Web.Features.Authentication.Login;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Authentication;

[Collection(DatabaseCollection.Name)]
public sealed class LoginTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string InvalidCredentialsMessage = "E-mail ou senha incorretos.";
    private const string LockedOutMessage = "Acesso bloqueado por 15 minutos";

    private readonly CapturingLoggerProvider _logs = new();
    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;

    public Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString, logs: _logs);
        _client = _factory.CreateBrowser();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData(AppRoles.Personal, "/personal")]
    [InlineData(AppRoles.Student, "/aluno")]
    public async Task Login_Succeeds_AndRedirectsToRoleHome(string role, string home)
    {
        var account = await _factory.CreateAccountAsync(role);

        var response = await _client.PostLoginAsync(account.Email, account.Password);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(home, response.RedirectTarget());
        var page = await _client.GetAsync(home);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Início", WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Login_SetsSecureHttpOnlyLaxCookie_WithoutExpiry_WhenNotRemembered()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);

        var response = await _client.PostLoginAsync(account.Email, account.Password);

        var cookie = AuthTestHelpers.AuthCookieHeader(response);
        Assert.NotNull(cookie);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithRememberMe_SetsPersistentCookieFor14Days()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);

        var response = await _client.PostLoginAsync(account.Email, account.Password, rememberMe: true);

        var cookie = AuthTestHelpers.AuthCookieHeader(response);
        Assert.NotNull(cookie);
        var expires = cookie.Split(';').Select(p => p.Trim())
            .Single(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))["expires=".Length..];
        var lifetime = DateTimeOffset.Parse(expires) - DateTimeOffset.UtcNow;
        Assert.InRange(lifetime.TotalDays, 13.9, 14.1);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShowsGenericMessage_AndDoesNotSignIn()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);

        var response = await _client.PostLoginAsync(account.Email, "Errada-123");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(InvalidCredentialsMessage, html);
        Assert.Null(AuthTestHelpers.AuthCookieHeader(response));
        Assert.DoesNotContain("Errada-123", html);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ShowsSameGenericMessage()
    {
        var response = await _client.PostLoginAsync($"ninguem-{Guid.NewGuid():N}@example.test", "Qualquer-123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(InvalidCredentialsMessage, await response.Content.ReadAsStringAsync());
        Assert.Null(AuthTestHelpers.AuthCookieHeader(response));
    }

    [Fact]
    public async Task Login_WithEmptyFields_ShowsFieldErrors()
    {
        var response = await _client.PostLoginAsync("", "");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Informe seu e-mail.", html);
        Assert.Contains("Informe sua senha.", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.DoesNotContain(InvalidCredentialsMessage, html);
    }

    [Fact]
    public async Task Login_WithMalformedEmail_ShowsFormatError()
    {
        var response = await _client.PostLoginAsync("sem-arroba", "Qualquer-123");

        // Texto vindo de código C# sai com acentos codificados (&#xE1;); decodifica para comparar.
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Informe um e-mail válido, como voce@exemplo.com.", html);
    }

    [Fact]
    public async Task Login_LocksAccountAfterFiveFailures_EvenWithCorrectPasswordAfterwards()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var failed = await _client.PostLoginAsync(account.Email, "Errada-123");
            Assert.Contains(InvalidCredentialsMessage, await failed.Content.ReadAsStringAsync());
        }

        var fifth = await _client.PostLoginAsync(account.Email, "Errada-123");
        Assert.Contains(LockedOutMessage, await fifth.Content.ReadAsStringAsync());

        var correct = await _client.PostLoginAsync(account.Email, account.Password);
        Assert.Contains(LockedOutMessage, await correct.Content.ReadAsStringAsync());
        Assert.Null(AuthTestHelpers.AuthCookieHeader(correct));

        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var lockoutEnd = await users.GetLockoutEndDateAsync((await users.FindByIdAsync(account.Id.ToString()))!);
        Assert.NotNull(lockoutEnd);
        Assert.InRange((lockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes, 14, 15.1);
    }

    [Fact]
    public async Task Login_InactivePersonal_GetsGenericMessage()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal, personalActive: false);

        var response = await _client.PostLoginAsync(account.Email, account.Password);

        Assert.Contains(InvalidCredentialsMessage, await response.Content.ReadAsStringAsync());
        Assert.Null(AuthTestHelpers.AuthCookieHeader(response));
    }

    [Fact]
    public async Task Login_HonorsLocalReturnUrl()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);

        var response = await _client.PostLoginAsync(account.Email, account.Password,
            path: "/entrar?ReturnUrl=%2Fpersonal%3Faba%3D1");

        Assert.Equal("/personal?aba=1", response.RedirectTarget());
    }

    [Theory]
    [InlineData("https://malicioso.example")]
    [InlineData("//malicioso.example")]
    [InlineData("/\\malicioso.example")]
    public async Task Login_IgnoresExternalReturnUrl(string returnUrl)
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);

        var response = await _client.PostLoginAsync(account.Email, account.Password,
            path: $"/entrar?ReturnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/personal", response.RedirectTarget());
    }

    [Fact]
    public async Task LoginPage_RedirectsAuthenticatedUserToHome()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Student);
        await _client.PostLoginAsync(account.Email, account.Password);

        var response = await _client.GetAsync("/entrar");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/aluno", response.RedirectTarget());
    }

    [Fact]
    public async Task Login_LogsFailuresWithUserIdButWithoutEmailOrPassword()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);

        await _client.PostLoginAsync(account.Email, "Errada-123");

        var loginLogs = _logs.Logs.Where(l => l.Category == typeof(LoginHandler).FullName).ToList();
        var failure = Assert.Single(loginLogs, l => l.Level == LogLevel.Warning);
        Assert.Contains(account.Id.ToString(), failure.Message);
        Assert.True(failure.Scope.ContainsKey("CorrelationId"));
        Assert.DoesNotContain(_logs.Logs, l => l.Message.Contains(account.Email, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_logs.Logs, l => l.Message.Contains("Errada-123"));
    }

    [Fact]
    public async Task Login_WithoutAntiforgeryToken_IsRejected()
    {
        var account = await _factory.CreateAccountAsync(AppRoles.Personal);

        var response = await _client.PostAsync("/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["Input.Email"] = account.Email,
            ["Input.Password"] = account.Password
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(AuthTestHelpers.AuthCookieHeader(response));
    }
}
