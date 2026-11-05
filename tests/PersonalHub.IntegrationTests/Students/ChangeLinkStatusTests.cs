using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Students.ChangeLinkStatus;

namespace PersonalHub.IntegrationTests.Students;

[Collection(DatabaseCollection.Name)]
public sealed class ChangeLinkStatusTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private LoggedPersonal _personal = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _personal = await _factory.LoginPersonalAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static string Confirm(Guid id) => $"/personal/alunos/{id}/vinculo";

    private Task<HttpResponseMessage> PostAsync(Guid id, string action) =>
        _personal.Client.PostFormAsync(Confirm(id), "vinculo", new Dictionary<string, string> { ["acao"] = action });

    [Fact]
    public async Task Inactivate_InvitedStudent_RevokesInviteKeepsDataAndAudits()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        var page = await (await _personal.Client.GetAsync(Confirm(invite.PersonalStudentId))).TextAsync();
        Assert.Contains("Inativar o vínculo com Mariana Lopes?", page);
        Assert.Contains("O histórico de aulas e pagamentos continua disponível.", page);
        Assert.Contains("O convite em aberto deixa de funcionar na hora.", page);

        var response = await PostAsync(invite.PersonalStudentId, "inativar");

        Assert.Equal($"/personal/alunos/{invite.PersonalStudentId}?aviso=inativado", response.RedirectTarget());
        var link = await _factory.LinkAsync(invite.PersonalStudentId);
        Assert.Equal(PersonalStudentStatus.Inactive, link.Status);
        Assert.NotNull(link.EndedAtUtc);
        Assert.Equal("Mariana Lopes", link.StudentName);
        Assert.NotNull(Assert.Single(await _factory.InvitesAsync(invite.PersonalStudentId)).RevokedAtUtc);
        Assert.Contains("Convite não encontrado", await _factory.InvitePageTextAsync(invite.Token));

        var audit = await _factory.WithDbAsync(db => db.AuditLogs.SingleAsync(
            a => a.EntityId == invite.PersonalStudentId && a.Action == "StudentLinkInactivated"));
        Assert.Equal(_personal.Account.Id, audit.ActorUserId);
        Assert.Contains("Invited", audit.Metadata);
    }

    [Fact]
    public async Task Inactivate_ActiveStudent_KeepsLessons()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        var studentUserId = await _factory.ActivateAsync(invite.PersonalStudentId);
        var lesson = await _factory.WithDbAsync(async db => await db.AddLessonAsync(await db.PersonalStudents.SingleAsync(s => s.Id == invite.PersonalStudentId)));

        await PostAsync(invite.PersonalStudentId, "inativar");

        var link = await _factory.LinkAsync(invite.PersonalStudentId);
        Assert.Equal(PersonalStudentStatus.Inactive, link.Status);
        Assert.Equal(studentUserId, link.StudentUserId);
        Assert.True(await _factory.WithDbAsync(db => db.Lessons.AnyAsync(l => l.Id == lesson.Id)));
        var details = await (await _personal.Client.GetAsync($"/personal/alunos/{invite.PersonalStudentId}?aviso=inativado")).TextAsync();
        Assert.Contains("Vínculo inativado.", details);
        Assert.Contains("Vínculo inativo desde", details);
    }

    [Fact]
    public async Task Reactivate_WithAccount_GoesBackToActive()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.ActivateAsync(invite.PersonalStudentId);
        await _factory.SetInactiveAsync(invite.PersonalStudentId, DateTime.UtcNow.AddDays(-2));
        var page = await (await _personal.Client.GetAsync(Confirm(invite.PersonalStudentId))).TextAsync();
        Assert.Contains("Reativar o vínculo com Mariana Lopes?", page);
        Assert.Contains("R$ 80,00 por aula, dia 10", page);
        Assert.Contains(">Reativar vínculo<", page);

        var response = await PostAsync(invite.PersonalStudentId, "reativar");

        Assert.Equal($"/personal/alunos/{invite.PersonalStudentId}?aviso=reativado", response.RedirectTarget());
        var link = await _factory.LinkAsync(invite.PersonalStudentId);
        Assert.Equal(PersonalStudentStatus.Active, link.Status);
        Assert.Null(link.EndedAtUtc);
        Assert.Single(await _factory.InvitesAsync(invite.PersonalStudentId));
        Assert.Contains("StudentLinkReactivated", await _factory.AuditActionsAsync(invite.PersonalStudentId));
    }

    [Fact]
    public async Task Reactivate_WithoutAccount_GoesBackToInvitedWithNewLink()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await PostAsync(invite.PersonalStudentId, "inativar");
        var page = await (await _personal.Client.GetAsync(Confirm(invite.PersonalStudentId))).TextAsync();
        Assert.Contains("Mariana ainda não criou a conta", page);
        Assert.Contains("Reativar e gerar convite", page);

        var response = await PostAsync(invite.PersonalStudentId, "reativar");

        Assert.Equal($"/personal/alunos/{invite.PersonalStudentId}/convite-criado", response.RedirectTarget());
        var created = await (await _personal.Client.GetAsync(response.RedirectTarget())).TextAsync();
        var newToken = StudentTestHelpers.InviteTokenIn(created)!;
        Assert.Contains("Personal Teste convidou você", await _factory.InvitePageTextAsync(newToken));
        Assert.Contains("Convite não encontrado", await _factory.InvitePageTextAsync(invite.Token));
        var link = await _factory.LinkAsync(invite.PersonalStudentId);
        Assert.Equal(PersonalStudentStatus.Invited, link.Status);
        Assert.Null(link.EndedAtUtc);
    }

    [Fact]
    public async Task Reactivate_WhenEmailIsInUseByAnotherLink_IsBlocked()
    {
        var old = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.SetInactiveAsync(old.PersonalStudentId, DateTime.UtcNow.AddDays(-5));
        await _factory.CreateInviteAsync(_personal.Account.Id, email: old.Email);

        var page = await (await _personal.Client.GetAsync(Confirm(old.PersonalStudentId))).TextAsync();
        Assert.Contains($"Você já tem outro aluno ativo com o e-mail <strong>{old.Email}</strong>. Inative aquele cadastro", page);
        Assert.Contains("antes de reativar este.", page);
        Assert.Contains(">Voltar<", page);
        Assert.DoesNotContain("Reativar e gerar convite", page);

        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ChangeLinkStatusHandler>();
        var result = await handler.ReactivateAsync(_personal.Account.Id, old.PersonalStudentId, null, CancellationToken.None);

        Assert.Equal(ChangeLinkStatusOutcome.EmailInUse, result.Outcome);
        Assert.Equal(PersonalStudentStatus.Inactive, (await _factory.LinkAsync(old.PersonalStudentId)).Status);
        Assert.DoesNotContain("StudentLinkReactivated", await _factory.AuditActionsAsync(old.PersonalStudentId));
    }

    [Fact]
    public async Task StaleInactivateRequest_OnInactiveLink_DoesNotReactivate()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.ActivateAsync(invite.PersonalStudentId);
        // A página foi aberta para inativar; outra aba inativou antes.
        await PostAsync(invite.PersonalStudentId, "inativar");

        var response = await PostAsync(invite.PersonalStudentId, "inativar");

        Assert.Equal($"/personal/alunos/{invite.PersonalStudentId}", response.RedirectTarget());
        Assert.Equal(PersonalStudentStatus.Inactive, (await _factory.LinkAsync(invite.PersonalStudentId)).Status);
        Assert.Single(await _factory.AuditActionsAsync(invite.PersonalStudentId), a => a == "StudentLinkInactivated");
    }

    [Fact]
    public async Task Handler_ParallelInactivations_AuditOnce()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ChangeLinkStatusHandler>();
            return await handler.InactivateAsync(_personal.Account.Id, invite.PersonalStudentId, null, CancellationToken.None);
        }));

        Assert.Single(results, r => r.Outcome == ChangeLinkStatusOutcome.Succeeded);
        Assert.Equal(3, results.Count(r => r.Outcome == ChangeLinkStatusOutcome.AlreadyDone));
        Assert.Single(await _factory.AuditActionsAsync(invite.PersonalStudentId), a => a == "StudentLinkInactivated");
    }

    [Fact]
    public async Task Handler_ParallelReactivationsOfTwoLinksWithSameEmail_OnlyOneWins()
    {
        var email = $"mesmo-{Guid.NewGuid():N}@example.test";
        var first = await _factory.CreateInviteAsync(_personal.Account.Id, email: email);
        await _factory.SetInactiveAsync(first.PersonalStudentId, DateTime.UtcNow);
        var second = await _factory.CreateInviteAsync(_personal.Account.Id, email: email);
        await _factory.SetInactiveAsync(second.PersonalStudentId, DateTime.UtcNow);

        var results = await Task.WhenAll(new[] { first, second }.Select(async link =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ChangeLinkStatusHandler>();
            return await handler.ReactivateAsync(_personal.Account.Id, link.PersonalStudentId, null, CancellationToken.None);
        }));

        Assert.Single(results, r => r.Outcome == ChangeLinkStatusOutcome.ReactivatedWithInvite);
        Assert.Single(results, r => r.Outcome == ChangeLinkStatusOutcome.EmailInUse);
        Assert.Equal(1, await _factory.WithDbAsync(db => db.PersonalStudents.CountAsync(
            s => s.StudentEmail == email && s.Status != PersonalStudentStatus.Inactive)));
    }

    [Fact]
    public async Task ConfirmPage_IsNotFoundForUnknownId()
    {
        var page = await _personal.Client.GetAsync(Confirm(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
    }
}
