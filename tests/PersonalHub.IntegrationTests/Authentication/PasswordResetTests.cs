using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Notifications;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.PhoneVerification;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Authentication;

/// <summary>Recuperação de senha por WhatsApp (D-058, fecha D-030).</summary>
[Collection(DatabaseCollection.Name)]
public sealed partial class PasswordResetTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string NewPassword = "Nova-Senha9";

    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;

    public Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _client = _factory.CreateBrowser();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private Task<HttpResponseMessage> RequestAsync(HttpClient client, string email) =>
        client.PostFormAsync("/esqueci-senha", "esqueci-senha", new Dictionary<string, string> { ["Input.Email"] = email });

    /// <summary>Pede o código e devolve o link "Já recebi o código".</summary>
    private async Task<string> RequestLinkAsync(string email)
    {
        var html = await (await RequestAsync(_client, email)).Content.ReadAsStringAsync();
        var match = NewPasswordLink().Match(html);
        Assert.True(match.Success, "Link para /nova-senha não encontrado.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private Task<HttpResponseMessage> ResetAsync(string link, string code, string password = NewPassword, string? confirm = null) =>
        _client.PostFormAsync(link, "nova-senha", new Dictionary<string, string>
        {
            ["Input.Code"] = code,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = confirm ?? password
        });

    private Task<string> CodeAsync(Guid userId) => _factory.LatestCodeAsync(PhoneCodePurpose.PasswordReset, userId);

    [Fact]
    public async Task Login_ShowsForgotPasswordLink_AndChangedBanner()
    {
        var login = await (await _client.GetAsync("/entrar")).TextAsync();
        Assert.Contains("href=\"/esqueci-senha\"", login);
        Assert.Contains("Esqueci minha senha", login);
        Assert.DoesNotContain("Senha alterada.", login);

        Assert.Contains("Senha alterada. Entre com a nova senha.",
            Regex.Replace(await (await _client.GetAsync("/entrar?senha=alterada")).TextAsync(), "<[^>]+>", ""));
    }

    [Fact]
    public async Task Request_AnswersTheSameWay_WhetherTheAccountExistsOrNot()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var known = await (await RequestAsync(_client, student.Email)).TextAsync();
        var unknown = await (await RequestAsync(_factory.CreateBrowser(), $"ninguem-{Guid.NewGuid():N}@example.test")).TextAsync();

        const string generic = "Se esse e-mail tiver uma conta, o código chega no WhatsApp em instantes. Ele vale por 10 minutos.";
        Assert.Contains(generic, known);
        Assert.Contains(generic, unknown);
        Assert.Equal(Normalize(known), Normalize(unknown));
        Assert.Contains("Já recebi o código", known);
        Assert.Contains("Trocou de número ou não recebeu? Fale com o seu personal ou com o suporte.", known);

        Assert.Equal(1, await _factory.WithDbAsync(db => db.PhoneVerificationCodes.CountAsync(c => c.UserId == student.Id)));
        // O e-mail não aparece em texto puro no link.
        Assert.DoesNotContain(student.Email, known);
    }

    /// <summary>Tira o que muda a cada resposta (antiforgery e o valor protegido do link).</summary>
    private static string Normalize(string html) =>
        RandomValues().Replace(html, "x");

    [Fact]
    public async Task Request_WithInvalidEmail_ShowsFieldError()
    {
        var html = await (await RequestAsync(_client, "nao-e-email")).TextAsync();

        Assert.Contains("Informe um e-mail válido, como voce@exemplo.com.", html);
    }

    [Fact]
    public async Task Request_AccountWithoutConfirmedPhone_SendsNothing()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student, phoneConfirmed: false);

        await RequestAsync(_client, student.Email);

        Assert.Equal(0, await _factory.WithDbAsync(db => db.PhoneVerificationCodes.CountAsync(c => c.UserId == student.Id)));
    }

    [Fact]
    public async Task Personal_GetsTheCodeOnTheProfilePhone()
    {
        var personal = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _factory.WithDbAsync(db => db.PersonalProfiles.Where(p => p.UserId == personal.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PhoneNumber, "+5511912340000")));

        await RequestAsync(_client, personal.Email);

        var code = await _factory.WithDbAsync(db => db.PhoneVerificationCodes.SingleAsync(c => c.UserId == personal.Id));
        Assert.Equal("+5511912340000", code.PhoneE164);
    }

    [Fact]
    public async Task Reset_WithRightCode_ChangesPassword_RotatesStamp_AndRedirectsToLogin()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var stampBefore = await StampAsync(student.Id);
        var link = await RequestLinkAsync(student.Email);

        var response = await ResetAsync(link, await CodeAsync(student.Id));

        Assert.Equal("/entrar?senha=alterada", response.RedirectTarget());
        Assert.NotEqual(stampBefore, await StampAsync(student.Id));
        Assert.Equal(HttpStatusCode.Redirect, (await _factory.CreateBrowser().PostLoginAsync(student.Email, NewPassword)).StatusCode);
        Assert.Contains("E-mail ou senha incorretos.",
            await (await _factory.CreateBrowser().PostLoginAsync(student.Email, student.Password)).TextAsync());

        var audit = await _factory.WithDbAsync(db => db.AuditLogs.SingleAsync(a => a.EntityId == student.Id && a.Action == "PasswordReset"));
        Assert.Equal(student.Id, audit.ActorUserId);
        Assert.DoesNotContain(student.Email, audit.Metadata);
    }

    [Fact]
    public async Task Reset_LogsOutOtherSessions()
    {
        // O cookie confere o security stamp a cada minuto; o relógio do validador avança para forçar a conferência.
        var clock = new MutableClock(DateTimeOffset.UtcNow);
        await using var factory = TestApp.Create(fixture.ConnectionString).WithWebHostBuilder(builder =>
            Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.ConfigureTestServices(builder, services =>
                services.Configure<SecurityStampValidatorOptions>(o => o.TimeProvider = clock)));
        var student = await factory.CreateAccountAsync(AppRoles.Student);
        var otherDevice = factory.CreateBrowser();
        await otherDevice.PostLoginAsync(student.Email, student.Password);
        Assert.Equal(HttpStatusCode.OK, (await otherDevice.GetAsync("/aluno")).StatusCode);

        var client = factory.CreateBrowser();
        var html = await (await RequestAsync(client, student.Email)).Content.ReadAsStringAsync();
        var link = WebUtility.HtmlDecode(NewPasswordLink().Match(html).Groups[1].Value);
        var code = await factory.LatestCodeAsync(PhoneCodePurpose.PasswordReset, student.Id);
        var reset = await client.PostFormAsync(link, "nova-senha", new Dictionary<string, string>
        {
            ["Input.Code"] = code, ["Input.Password"] = NewPassword, ["Input.ConfirmPassword"] = NewPassword
        });
        Assert.Equal("/entrar?senha=alterada", reset.RedirectTarget());

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.StartsWith("/entrar", (await otherDevice.GetAsync("/aluno")).RedirectTarget());
    }

    [Fact]
    public async Task Reset_UnlocksALockedAccount()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        for (var i = 0; i < 5; i++)
        {
            await _factory.CreateBrowser().PostLoginAsync(student.Email, "Errada-123");
        }

        await ResetAsync(await RequestLinkAsync(student.Email), await CodeAsync(student.Id));

        Assert.Equal(HttpStatusCode.Redirect, (await _factory.CreateBrowser().PostLoginAsync(student.Email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_WithWrongCode_ShowsRemainingAttempts_AndKeepsPassword()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var link = await RequestLinkAsync(student.Email);
        var code = await CodeAsync(student.Id);

        var html = await (await ResetAsync(link, ((int.Parse(code) + 1) % 1_000_000).ToString("D6"))).TextAsync();

        Assert.Contains("Código incorreto. Você ainda tem 4 tentativas.", html);
        Assert.DoesNotContain(NewPassword, html);
        Assert.Equal(HttpStatusCode.Redirect, (await _factory.CreateBrowser().PostLoginAsync(student.Email, student.Password)).StatusCode);
    }

    [Fact]
    public async Task Reset_WithInvalidPassword_DoesNotSpendTheCode()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var link = await RequestLinkAsync(student.Email);
        var code = await CodeAsync(student.Id);

        Assert.Contains("A senha precisa ter pelo menos um número.", await (await ResetAsync(link, code, "semnumeroA")).TextAsync());
        Assert.Contains("As senhas não são iguais.", await (await ResetAsync(link, code, NewPassword, "Outra-Senha1")).TextAsync());

        Assert.Equal("/entrar?senha=alterada", (await ResetAsync(link, code)).RedirectTarget());
    }

    [Fact]
    public async Task Reset_ForUnknownEmail_ShowsExpired()
    {
        var link = await RequestLinkAsync($"ninguem-{Guid.NewGuid():N}@example.test");

        var html = await (await ResetAsync(link, "123456")).TextAsync();

        Assert.Contains("Esse código expirou.", html);
        Assert.Contains("href=\"/esqueci-senha\"", html);
    }

    [Fact]
    public async Task Reset_CodeUsedOrExpired_ShowsExpiredWithLinkForNewCode()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var link = await RequestLinkAsync(student.Email);
        var code = await CodeAsync(student.Id);
        await ResetAsync(link, code);

        var html = await (await ResetAsync(link, code, "Outra-Senha7")).TextAsync();

        Assert.Contains("Esse código expirou.", html);
        Assert.Contains("Pedir um código novo", html);
    }

    [Theory]
    [InlineData("/nova-senha")]
    [InlineData("/nova-senha?r=adulterado")]
    public async Task NewPasswordPage_WithoutValidLink_GoesBackToForgotPassword(string path)
    {
        Assert.Equal("/esqueci-senha", (await _client.GetAsync(path)).RedirectTarget());
    }

    [Fact]
    public async Task NewPasswordPage_ShowsTheRulesAndTheOtherDevicesNote()
    {
        var html = await (await _client.GetAsync(await RequestLinkAsync("alguem@example.test"))).TextAsync();

        Assert.Contains("Salvar nova senha", html);
        Assert.Contains("Ao salvar, você sai da conta em todos os outros aparelhos.", html);
        Assert.Contains("8 ou mais caracteres", html);
        Assert.Contains("autocomplete=\"one-time-code\"", html);
    }

    [Fact]
    public async Task ResetPages_AreRateLimitedPerIp()
    {
        await using var factory = TestApp.Create(fixture.ConnectionString,
            new Dictionary<string, string?> { ["PhoneVerification:RateLimit:PermitLimit"] = "2" });
        var client = factory.CreateBrowser();

        await client.GetAsync("/esqueci-senha");
        await client.GetAsync("/esqueci-senha");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/esqueci-senha")).StatusCode);
    }

    private Task<string?> StampAsync(Guid userId) =>
        _factory.WithDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp).SingleAsync());

    [GeneratedRegex("href=\"(/nova-senha\\?r=[^\"]+)\"")]
    private static partial Regex NewPasswordLink();

    [GeneratedRegex("[A-Za-z0-9+/_%=-]{40,}")]
    private static partial Regex RandomValues();
}
