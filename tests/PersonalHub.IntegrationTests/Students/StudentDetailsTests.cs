using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Students.ManageInvite;

namespace PersonalHub.IntegrationTests.Students;

[Collection(DatabaseCollection.Name)]
public sealed class StudentDetailsTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private LoggedPersonal _personal = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _personal = await _factory.LoginPersonalAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static string Details(Guid id) => $"/personal/alunos/{id}";

    [Fact]
    public async Task Details_OfInvitedStudent_ShowsInviteCardContactAndBilling()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);

        var page = await _personal.Client.GetAsync(Details(invite.PersonalStudentId));
        var html = await page.TextAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Mariana Lopes", html);
        Assert.Contains(">ML<", html);
        Assert.Contains("ph-pill-pending\">Convidado<", html);
        Assert.Contains("Aguardando aceite", html);
        Assert.Contains("Gerar novo link e enviar", html);
        Assert.Contains("O link anterior deixa de funcionar na hora.", html);
        Assert.Contains("Cancelar convite", html);
        Assert.Contains("E-mail (login)", html);
        Assert.Contains(invite.Email, html);
        Assert.Contains("(11) 98765-4321", html);
        Assert.Contains("href=\"https://wa.me/5511987654321\"", html);
        Assert.Contains("R$ 80,00", html);
        Assert.Contains("Dia 10", html);
        Assert.Contains($"href=\"/personal/alunos/{invite.PersonalStudentId}/editar\"", html);
        Assert.Contains("Inativar vínculo", html);
        Assert.DoesNotContain("aparece aqui a partir da Fase 3", html);
    }

    [Fact]
    public async Task Details_OfExpiredInvite_HidesCancelButton()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.SetInviteAsync(invite.PersonalStudentId, expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var html = await (await _personal.Client.GetAsync(Details(invite.PersonalStudentId))).TextAsync();

        Assert.Contains(">Expirado<", html);
        Assert.Contains("sem ser usado", html);
        Assert.Contains("Gerar novo link e enviar", html);
        Assert.DoesNotContain("Cancelar convite", html);
    }

    [Fact]
    public async Task Details_OfActiveStudent_ShowsLessonsSummaryAndNoInviteCard()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.ActivateAsync(invite.PersonalStudentId);

        var html = await (await _personal.Client.GetAsync(Details(invite.PersonalStudentId))).TextAsync();

        Assert.Contains("ph-pill-success\">Ativo<", html);
        Assert.Contains($"href=\"/personal/aulas/nova?aluno={invite.PersonalStudentId}\"", html);
        Assert.Contains($"href=\"/personal/aulas?aluno={invite.PersonalStudentId}\"", html);
        Assert.DoesNotContain("Gerar novo link", html);
        Assert.Contains("Inativar vínculo", html);
    }

    [Fact]
    public async Task Details_OfInactiveStudent_ShowsNoticeAndReactivate()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.SetInactiveAsync(invite.PersonalStudentId, new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc));

        var html = await (await _personal.Client.GetAsync(Details(invite.PersonalStudentId))).TextAsync();

        Assert.Contains("Vínculo inativo desde 01/10/2026.", html);
        Assert.Contains("Novas aulas não podem ser registradas.", html);
        Assert.Contains("Reativar vínculo", html);
        Assert.DoesNotContain("Inativar vínculo", html);
        Assert.DoesNotContain("Gerar novo link", html);
    }

    [Fact]
    public async Task Renew_RevokesOldLinkAtOnce_AndShowsTheNewOneOnce()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);

        var response = await _personal.Client.PostFormAsync(Details(invite.PersonalStudentId), "novo-link", []);

        Assert.Equal($"/personal/alunos/{invite.PersonalStudentId}/convite-criado", response.RedirectTarget());
        var page = await (await _personal.Client.GetAsync(response.RedirectTarget())).TextAsync();
        Assert.Contains("Novo link para Mariana Lopes", page);
        Assert.Contains("O link anterior não funciona mais.", page);
        var newToken = StudentTestHelpers.InviteTokenIn(page);
        Assert.NotNull(newToken);
        Assert.NotEqual(invite.Token, newToken);

        // O link antigo cai na tela de convite inválido; o novo funciona.
        Assert.Contains("Convite não encontrado", await _factory.InvitePageTextAsync(invite.Token));
        Assert.Contains("Personal Teste convidou você", await _factory.InvitePageTextAsync(newToken));

        var invites = await _factory.InvitesAsync(invite.PersonalStudentId);
        Assert.Equal(2, invites.Count);
        Assert.NotNull(invites[0].RevokedAtUtc);
        Assert.Null(invites[1].RevokedAtUtc);
        Assert.Equal(invites[1].TokenHash, InviteToken.TryHash(newToken));

        var audit = await _factory.WithDbAsync(db => db.AuditLogs.SingleAsync(
            a => a.EntityId == invite.PersonalStudentId && a.Action == "StudentInviteRenewed"));
        Assert.Equal(_personal.Account.Id, audit.ActorUserId);
        Assert.Contains("\"revokedInvites\": 1", audit.Metadata);
        Assert.DoesNotContain(newToken, audit.Metadata);
        Assert.DoesNotContain(invite.Email, audit.Metadata);
    }

    [Fact]
    public async Task Renew_AfterExpiry_CreatesValidLink()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.SetInviteAsync(invite.PersonalStudentId, expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));

        var response = await _personal.Client.PostFormAsync(Details(invite.PersonalStudentId), "novo-link", []);
        var page = await (await _personal.Client.GetAsync(response.RedirectTarget())).TextAsync();

        Assert.Contains("Personal Teste convidou você", await _factory.InvitePageTextAsync(StudentTestHelpers.InviteTokenIn(page)!));
    }

    [Fact]
    public async Task Cancel_RevokesTheLink_AuditsOnce_AndShowsCancelledState()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);

        var response = await _personal.Client.PostFormAsync(Details(invite.PersonalStudentId), "cancelar-convite", []);

        Assert.Equal($"{Details(invite.PersonalStudentId)}?aviso=convite-cancelado", response.RedirectTarget());
        var html = await (await _personal.Client.GetAsync(response.RedirectTarget())).TextAsync();
        Assert.Contains("Convite cancelado. O link enviado não funciona mais.", html);
        Assert.Contains(">Cancelado<", html);
        Assert.DoesNotContain("Cancelar convite", html);
        Assert.Contains("Gerar novo link e enviar", html);
        Assert.Contains("Convite não encontrado", await _factory.InvitePageTextAsync(invite.Token));
        Assert.Equal(PersonalStudentStatus.Invited, (await _factory.LinkAsync(invite.PersonalStudentId)).Status);

        // Repetir (duplo toque) não audita de novo.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<InviteActionsHandler>();
            var again = await handler.CancelAsync(_personal.Account.Id, invite.PersonalStudentId, null, CancellationToken.None);
            Assert.Equal(InviteActionOutcome.Succeeded, again.Outcome);
        }

        Assert.Single(await _factory.AuditActionsAsync(invite.PersonalStudentId), a => a == "StudentInviteCancelled");
    }

    [Fact]
    public async Task InviteActions_OnActiveStudent_ChangeNothing()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.ActivateAsync(invite.PersonalStudentId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<InviteActionsHandler>();
        var renew = await handler.RenewAsync(_personal.Account.Id, invite.PersonalStudentId, null, CancellationToken.None);
        var cancel = await handler.CancelAsync(_personal.Account.Id, invite.PersonalStudentId, null, CancellationToken.None);

        Assert.Equal(InviteActionOutcome.NotInvited, renew.Outcome);
        Assert.Null(renew.InviteToken);
        Assert.Equal(InviteActionOutcome.NotInvited, cancel.Outcome);
        var only = Assert.Single(await _factory.InvitesAsync(invite.PersonalStudentId));
        Assert.Null(only.RevokedAtUtc);
    }

    [Fact]
    public async Task Renew_InParallel_LeavesExactlyOneOpenInvite()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<InviteActionsHandler>();
            return await handler.RenewAsync(_personal.Account.Id, invite.PersonalStudentId, null, CancellationToken.None);
        }));

        Assert.All(results, r => Assert.Equal(InviteActionOutcome.Succeeded, r.Outcome));
        var invites = await _factory.InvitesAsync(invite.PersonalStudentId);
        Assert.Equal(5, invites.Count);
        var open = Assert.Single(invites, i => i.RevokedAtUtc is null);
        Assert.Contains(results, r => InviteToken.TryHash(r.InviteToken)!.SequenceEqual(open.TokenHash));
    }

    [Fact]
    public async Task Renew_AfterStudentAcceptedMeanwhile_ShowsCurrentState()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        var page = await _personal.Client.GetAsync(Details(invite.PersonalStudentId));
        var token = AuthTestHelpers.AntiforgeryToken(await page.Content.ReadAsStringAsync());
        await _factory.ActivateAsync(invite.PersonalStudentId);

        var response = await _personal.Client.PostAsync(Details(invite.PersonalStudentId), new FormUrlEncodedContent(
            new Dictionary<string, string> { ["_handler"] = "novo-link", ["__RequestVerificationToken"] = token }));

        // O formulário não existe mais na página (aluno ativo): nada é gerado.
        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Single(await _factory.InvitesAsync(invite.PersonalStudentId));
    }
}
