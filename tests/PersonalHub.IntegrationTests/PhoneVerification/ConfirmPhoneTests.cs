using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Lessons;
using PersonalHub.IntegrationTests.Notifications;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.PhoneVerification;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.PhoneVerification;

/// <summary>Aluno que aceitou o convite antes da Fase 6 confirma o celular antes de usar o app (D-059).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConfirmPhoneTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string ConfirmPath = "/aluno/confirmar-celular";

    private WebApplicationFactory<Program> _factory = default!;
    private TestAccount _personal = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _personal = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _factory.WithDbAsync(db => db.PersonalProfiles.Where(p => p.UserId == _personal.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, "Carlos Lima")));
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>Aluno antigo: conta sem celular confirmado ligada a um vínculo com o celular dado.</summary>
    private async Task<(TestAccount Account, string Phone)> OldStudentAsync(string? phone = null)
    {
        phone ??= StudentTestHelpers.RandomPhone();
        var invite = await _factory.CreateInviteAsync(_personal.Id, phone: phone);
        var account = await _factory.CreateAccountAsync(AppRoles.Student, phoneConfirmed: false);
        await _factory.LinkStudentAsync(invite.PersonalStudentId, account);
        return (account, "+55" + phone);
    }

    [Fact]
    public async Task OldStudent_IsSentToConfirmPhone_FromAnyStudentPage()
    {
        var (account, _) = await OldStudentAsync("11987650123");
        var client = _factory.CreateBrowser();

        Assert.Equal("/aluno", (await client.PostLoginAsync(account.Email, account.Password)).RedirectTarget());
        Assert.Equal(ConfirmPath, (await client.GetAsync("/aluno")).RedirectTarget());
        Assert.Equal(ConfirmPath, (await client.GetAsync("/aluno/aulas")).RedirectTarget());
        Assert.Equal(ConfirmPath, (await client.GetAsync("/aluno/pagamentos")).RedirectTarget());

        var html = await (await client.GetAsync(ConfirmPath)).TextAsync();
        Assert.Contains("Confirme seu WhatsApp", html);
        Assert.Contains("WhatsApp cadastrado pelo Carlos", html);
        Assert.Contains("(11) 9••••-0123", html);
        Assert.Contains("Enviar código pelo WhatsApp", html);
    }

    [Fact]
    public async Task Confirming_SetsThePhone_RefreshesTheCookie_AndOpensTheApp()
    {
        var (account, phone) = await OldStudentAsync();
        var client = _factory.CreateBrowser();
        await client.PostLoginAsync(account.Email, account.Password);

        var sent = await (await client.PostFormAsync(ConfirmPath, "enviar-codigo", new Dictionary<string, string>())).TextAsync();
        Assert.Contains("Confirmar código", sent);
        var code = await _factory.LatestCodeAsync(PhoneCodePurpose.PhoneConfirmation, account.Id);
        var outbox = await _factory.WithDbAsync(db => db.NotificationOutbox.AsNoTracking()
            .Where(o => o.RecipientUserId == account.Id).ToListAsync());
        Assert.Equal(phone, Assert.Single(outbox).RecipientPhone);

        var confirmed = await client.PostFormAsync(ConfirmPath, "confirmar-codigo", new Dictionary<string, string> { ["Code.Value"] = code });

        Assert.Equal("/aluno", confirmed.RedirectTarget());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/aluno")).StatusCode);
        var user = await _factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == account.Id));
        Assert.Equal(phone, user.PhoneNumber);
        Assert.True(user.PhoneNumberConfirmed);
        Assert.True(await _factory.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.EntityId == account.Id && a.Action == "PhoneConfirmed")));
    }

    [Fact]
    public async Task WrongCode_KeepsTheGate()
    {
        var (account, _) = await OldStudentAsync();
        var client = _factory.CreateBrowser();
        await client.PostLoginAsync(account.Email, account.Password);
        await client.PostFormAsync(ConfirmPath, "enviar-codigo", new Dictionary<string, string>());
        var code = await _factory.LatestCodeAsync(PhoneCodePurpose.PhoneConfirmation, account.Id);

        var html = await (await client.PostFormAsync(ConfirmPath, "confirmar-codigo",
            new Dictionary<string, string> { ["Code.Value"] = ((int.Parse(code) + 1) % 1_000_000).ToString("D6") })).TextAsync();

        Assert.Contains("Código incorreto. Você ainda tem 4 tentativas.", html);
        Assert.Equal(ConfirmPath, (await client.GetAsync("/aluno")).RedirectTarget());
    }

    [Fact]
    public async Task UsesThePhoneOfTheMostRecentLink()
    {
        var (account, _) = await OldStudentAsync("11911110000");
        var otherPersonal = await _factory.CreateAccountAsync(AppRoles.Personal);
        var second = await _factory.CreateInviteAsync(otherPersonal.Id, phone: "21922220000");
        await _factory.LinkStudentAsync(second.PersonalStudentId, account);
        await _factory.WithDbAsync(db => db.PersonalStudents.Where(s => s.Id == second.PersonalStudentId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActivatedAtUtc, DateTime.UtcNow.AddMinutes(5))));
        var client = _factory.CreateBrowser();
        await client.PostLoginAsync(account.Email, account.Password);

        Assert.Contains("(21) 9••••-0000", await (await client.GetAsync(ConfirmPath)).TextAsync());
    }

    [Fact]
    public async Task ConfirmedStudent_IsNotBlocked_AndTheConfirmPageSendsHome()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        var account = await _factory.LinkStudentAsync(invite.PersonalStudentId);
        var client = _factory.CreateBrowser();
        await client.PostLoginAsync(account.Email, account.Password);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/aluno")).StatusCode);
        Assert.Equal("/aluno", (await client.GetAsync(ConfirmPath)).RedirectTarget());
    }

    [Fact]
    public async Task StaleCookie_IsRefreshed_WhenThePhoneWasConfirmedElsewhere()
    {
        var (account, phone) = await OldStudentAsync();
        var client = _factory.CreateBrowser();
        await client.PostLoginAsync(account.Email, account.Password);
        await _factory.WithDbAsync(db => db.Users.Where(u => u.Id == account.Id).ExecuteUpdateAsync(s => s
            .SetProperty(u => u.PhoneNumber, phone).SetProperty(u => u.PhoneNumberConfirmed, true)));

        Assert.Equal("/aluno", (await client.GetAsync(ConfirmPath)).RedirectTarget());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/aluno")).StatusCode);
    }

    [Fact]
    public async Task PersonalWhoIsAlsoStudent_IsNotBlockedInThePersonalArea()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        var otherPersonal = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _factory.LinkStudentAsync(invite.PersonalStudentId, otherPersonal);
        await _factory.WithDbAsync(async db =>
        {
            var roleId = AppRoles.StudentId;
            db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = otherPersonal.Id, RoleId = roleId });
            return await db.SaveChangesAsync();
        });
        var client = _factory.CreateBrowser();
        await client.PostLoginAsync(otherPersonal.Email, otherPersonal.Password);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/personal")).StatusCode);
    }
}
