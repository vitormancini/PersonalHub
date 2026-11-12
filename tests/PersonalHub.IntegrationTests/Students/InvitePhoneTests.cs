using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Notifications;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Domain.PhoneVerification;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Students;

/// <summary>
/// Código do WhatsApp no aceite do convite (D-057, fecha D-033): nada é enviado ao abrir o link, o código vai ao
/// celular cadastrado pelo personal e nenhum dos dois formulários conclui sem a prova do código.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class InvitePhoneTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Password = "Aluna-Forte1";

    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;
    private TestAccount _personal = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _client = _factory.CreateBrowser();
        _personal = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _factory.WithDbAsync(db => db.PersonalProfiles.Where(p => p.UserId == _personal.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, "Carlos Lima")));
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private Task<HttpResponseMessage> SendCodeAsync(string token) =>
        _client.PostFormAsync($"/convite/{token}", "enviar-codigo", new Dictionary<string, string>());

    private Task<HttpResponseMessage> ConfirmCodeAsync(string token, string code) =>
        _client.PostFormAsync($"/convite/{token}", "confirmar-codigo", new Dictionary<string, string> { ["Code.Value"] = code });

    private static Dictionary<string, string> NewAccount(string? proof) => new()
    {
        ["New.Name"] = "Mariana Lopes",
        ["New.Password"] = Password,
        ["New.ConfirmPassword"] = Password,
        ["New.AcceptTerms"] = "true",
        ["New.PhoneProof"] = proof ?? string.Empty
    };

    private Task<int> CodeRowsAsync(Guid personalStudentId) =>
        _factory.WithDbAsync(db => db.PhoneVerificationCodes.CountAsync(c => c.PersonalStudentId == personalStudentId));

    [Fact]
    public async Task OpeningTheLink_SendsNothing_AndShowsTheMaskedPhone()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id, phone: "11987652468");

        var html = await (await _client.GetAsync($"/convite/{invite.Token}")).TextAsync();
        await _client.GetAsync($"/convite/{invite.Token}");

        Assert.Contains("Carlos Lima convidou você", html);
        Assert.Contains("Primeiro, confirme que este WhatsApp é seu.", html);
        Assert.Contains("WhatsApp cadastrado pelo Carlos", html);
        Assert.Contains("(11) 9••••-2468", html);
        Assert.DoesNotContain("98765", html);
        Assert.Contains("Ao continuar, você aceita receber pelo WhatsApp os avisos das suas aulas e pagamentos.", html);
        Assert.Contains("Esse não é o seu número? Peça ao Carlos para corrigir o seu celular e enviar o convite de novo.", html);
        Assert.Equal(0, await CodeRowsAsync(invite.PersonalStudentId));
    }

    [Fact]
    public async Task SendCode_GoesToThePhoneTheTrainerRegistered_AndShowsTheCodeStep()
    {
        var phone = StudentTestHelpers.RandomPhone();
        var invite = await _factory.CreateInviteAsync(_personal.Id, phone: phone);

        var html = await (await SendCodeAsync(invite.Token)).TextAsync();

        Assert.Contains("Código de 6 números", html);
        Assert.Contains("autocomplete=\"one-time-code\"", html);
        Assert.Contains("inputmode=\"numeric\"", html);
        Assert.Contains("maxlength=\"6\"", html);
        Assert.Contains("Confirmar código", html);
        Assert.Contains("Reenviar código em 1:00", html);
        Assert.Contains("data-ph-countdown=\"60\"", html);

        var row = Assert.Single(await _factory.WithDbAsync(db =>
            db.NotificationOutbox.Where(o => o.EventType == NotificationTemplates.Code && o.RecipientPhone == "+55" + phone)
                .ToListAsync()));
        Assert.Null(row.RecipientUserId);
        Assert.True(row.IsAuthentication);

        // Recarregar a página continua no passo do código, sem enviar outro.
        var again = await (await _client.GetAsync($"/convite/{invite.Token}")).TextAsync();
        Assert.Contains("Confirmar código", again);
        Assert.Equal(1, await CodeRowsAsync(invite.PersonalStudentId));
    }

    [Fact]
    public async Task WrongCode_ShowsRemainingAttempts_ThenLocks()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        await SendCodeAsync(invite.Token);
        var code = await _factory.LatestCodeAsync(PhoneCodePurpose.InviteAcceptance, invite.PersonalStudentId);
        var wrong = ((int.Parse(code) + 1) % 1_000_000).ToString("D6");

        Assert.Contains("Código incorreto. Você ainda tem 4 tentativas.", await (await ConfirmCodeAsync(invite.Token, wrong)).TextAsync());
        for (var i = 0; i < 3; i++)
        {
            await ConfirmCodeAsync(invite.Token, wrong);
        }

        var locked = await (await ConfirmCodeAsync(invite.Token, wrong)).TextAsync();
        Assert.Contains("Muitas tentativas. Peça um código novo daqui a 15 minutos.", locked);

        var withRightCode = await (await ConfirmCodeAsync(invite.Token, code)).TextAsync();
        Assert.Contains("Muitas tentativas.", withRightCode);
        Assert.Null(NotificationTestHelpers.ProofIn(withRightCode));
    }

    [Fact]
    public async Task RightCode_ShowsTheAccountForm_AndCreatingTheAccountConfirmsThePhone()
    {
        var phone = StudentTestHelpers.RandomPhone();
        var invite = await _factory.CreateInviteAsync(_personal.Id, phone: phone);

        var proof = await _client.VerifyInvitePhoneAsync(_factory, invite.Token);
        var response = await _client.PostFormAsync($"/convite/{invite.Token}", "nova-conta", NewAccount(proof));

        Assert.Equal("/aluno", response.RedirectTarget());
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/aluno")).StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(invite.Email))!;
        Assert.Equal("+55" + phone, user.PhoneNumber);
        Assert.True(user.PhoneNumberConfirmed);
        var audit = await _factory.WithDbAsync(db => db.AuditLogs.SingleAsync(a => a.EntityId == invite.PersonalStudentId && a.Action == "InviteAccepted"));
        Assert.Contains("\"phoneVerified\":true", audit.Metadata!.Replace(" ", ""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("prova-falsa")]
    public async Task NewAccount_WithoutValidProof_CreatesNothing(string proof)
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);

        var response = await _client.PostFormAsync($"/convite/{invite.Token}", "nova-conta", NewAccount(proof));
        var html = await response.TextAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Confirme o código do WhatsApp para continuar.", html);
        Assert.Contains("Enviar código pelo WhatsApp", html);
        Assert.Null(AuthTestHelpers.AuthCookieHeader(response));
        await AssertNotAcceptedAsync(invite);
    }

    [Fact]
    public async Task NewAccount_WithProofOfAnotherInvite_CreatesNothing()
    {
        var other = await _factory.CreateInviteAsync(_personal.Id);
        var proof = await _client.VerifyInvitePhoneAsync(_factory, other.Token);
        var invite = await _factory.CreateInviteAsync(_personal.Id);

        var response = await _client.PostFormAsync($"/convite/{invite.Token}", "nova-conta", NewAccount(proof));

        Assert.Contains("Confirme o código do WhatsApp para continuar.", await response.TextAsync());
        await AssertNotAcceptedAsync(invite);
    }

    [Fact]
    public async Task Proof_StopsWorking_WhenTheTrainerChangesThePhone()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        var proof = await _client.VerifyInvitePhoneAsync(_factory, invite.Token);
        await _factory.WithDbAsync(db => db.PersonalStudents.Where(s => s.Id == invite.PersonalStudentId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StudentPhone, "+5521999998888")));

        var response = await _client.PostFormAsync($"/convite/{invite.Token}", "nova-conta", NewAccount(proof));

        Assert.Contains("Confirme o código do WhatsApp para continuar.", await response.TextAsync());
        await AssertNotAcceptedAsync(invite);
    }

    [Fact]
    public async Task Proof_ExpiresAfter15Minutes()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        var proof = await _factory.InviteProofAsync(invite.Token);
        await using var later = _factory.WithWhatsApp(new FakeWhatsAppSender(),
            new Lessons.FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(16)));
        var client = later.CreateBrowser();

        var response = await client.PostFormAsync($"/convite/{invite.Token}", "nova-conta", NewAccount(proof));

        Assert.Contains("Confirme o código do WhatsApp para continuar.", await response.TextAsync());
        await AssertNotAcceptedAsync(invite);
    }

    [Fact]
    public async Task ExistingAccount_WithoutProof_IsNotLinked_AndThePasswordIsNotEvenChecked()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var invite = await _factory.CreateInviteAsync(_personal.Id, email: student.Email);

        var response = await _client.PostFormAsync($"/convite/{invite.Token}", "conta-existente", new Dictionary<string, string>
        {
            ["Existing.Password"] = "Errada-123",
            ["Existing.AcceptTerms"] = "true",
            ["Existing.PhoneProof"] = ""
        });

        Assert.Contains("Confirme o código do WhatsApp para continuar.", await response.TextAsync());
        await AssertNotAcceptedAsync(invite);
        Assert.Equal(0, await _factory.WithDbAsync(db => db.Users.Where(u => u.Id == student.Id).Select(u => u.AccessFailedCount).SingleAsync()));
    }

    [Fact]
    public async Task ExistingAccount_WithProof_LinksAndTakesTheVerifiedPhone()
    {
        var student = await _factory.CreateAccountAsync(AppRoles.Student);
        var invite = await _factory.CreateInviteAsync(_personal.Id, email: student.Email, phone: "21987650001");

        var proof = await _client.VerifyInvitePhoneAsync(_factory, invite.Token);
        var response = await _client.PostFormAsync($"/convite/{invite.Token}", "conta-existente", new Dictionary<string, string>
        {
            ["Existing.Password"] = student.Password,
            ["Existing.AcceptTerms"] = "true",
            ["Existing.PhoneProof"] = proof
        });

        Assert.Equal("/aluno", response.RedirectTarget());
        var user = await _factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == student.Id));
        Assert.Equal("+5521987650001", user.PhoneNumber);
        Assert.True(user.PhoneNumberConfirmed);
        Assert.Equal(PersonalStudentStatus.Active, (await _factory.LinkAsync(invite.PersonalStudentId)).Status);
    }

    [Fact]
    public async Task SendCode_ForExpiredInvite_SendsNothing()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Id);
        var page = await _client.GetAsync($"/convite/{invite.Token}");
        var antiforgery = AuthTestHelpers.AntiforgeryToken(await page.Content.ReadAsStringAsync());
        await _factory.SetInviteAsync(invite.PersonalStudentId, expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var response = await _client.PostAsync($"/convite/{invite.Token}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "enviar-codigo",
            ["__RequestVerificationToken"] = antiforgery
        }));

        Assert.Contains("Este convite expirou", await response.TextAsync());
        Assert.Equal(0, await CodeRowsAsync(invite.PersonalStudentId));
    }

    private async Task AssertNotAcceptedAsync(CreatedInvite invite)
    {
        var link = await _factory.LinkAsync(invite.PersonalStudentId);
        Assert.Equal(PersonalStudentStatus.Invited, link.Status);
        Assert.Null(link.StudentUserId);
        Assert.Null((await _factory.WithDbAsync(db => db.StudentInvites.SingleAsync(i => i.PersonalStudentId == invite.PersonalStudentId))).UsedAtUtc);
    }
}
