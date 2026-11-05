using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Features.Students.CreateStudent;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Authentication;

/// <summary>Personal desativado depois do login: o cookie continua válido, mas a área do personal não.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ActivePersonalTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;
    private TestAccount _personal = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _client = _factory.CreateBrowser();
        _personal = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _client.PostLoginAsync(_personal.Email, _personal.Password);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData("/personal")]
    [InlineData("/personal/alunos/novo")]
    public async Task DeactivatedPersonal_WithValidCookie_IsSignedOutAndSentToLogin(string path)
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(path)).StatusCode);
        await DeactivateAsync();

        var response = await _client.GetAsync(path);

        Assert.Equal("/entrar", response.RedirectTarget());
        var cleared = AuthTestHelpers.AuthCookieHeader(response);
        Assert.NotNull(cleared);
        Assert.Contains("expires=Thu, 01 Jan 1970", cleared, StringComparison.OrdinalIgnoreCase);

        // Sem sessão: a próxima tentativa é de um anônimo.
        Assert.StartsWith("/entrar?ReturnUrl=", (await _client.GetAsync(path)).RedirectTarget());
    }

    [Fact]
    public async Task DeactivatedPersonal_CannotPostNewStudent()
    {
        // Token obtido enquanto ativo, como um formulário que ficou aberto no browser.
        var page = await _client.GetAsync("/personal/alunos/novo");
        var token = AuthTestHelpers.AntiforgeryToken(await page.Content.ReadAsStringAsync());
        await DeactivateAsync();

        var form = new Dictionary<string, string>(StudentTestHelpers.ValidStudentFields())
        {
            ["_handler"] = "novo-aluno",
            ["__RequestVerificationToken"] = token
        };
        var response = await _client.PostAsync("/personal/alunos/novo", new FormUrlEncodedContent(form));

        Assert.Equal("/entrar", response.RedirectTarget());
        Assert.Equal(0, await _factory.WithDbAsync(db => db.PersonalStudents.CountAsync(s =>
            db.PersonalProfiles.Any(p => p.Id == s.PersonalId && p.UserId == _personal.Id))));
    }

    [Fact]
    public async Task ActivePersonal_KeepsAccess()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/personal")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/personal/alunos")).StatusCode);
    }

    [Fact]
    public async Task InviteCreatedQuery_IgnoresInactivePersonal()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        await DeactivateAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<InviteCreatedQuery>();

        Assert.Null(await query.GetAsync(_personal.Id, invite.PersonalStudentId, CancellationToken.None));
    }

    private Task DeactivateAsync() =>
        _factory.WithDbAsync(db => db.PersonalProfiles.Where(p => p.UserId == _personal.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false)));
}
