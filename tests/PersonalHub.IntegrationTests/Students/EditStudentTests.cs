using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Students.EditStudent;

namespace PersonalHub.IntegrationTests.Students;

[Collection(DatabaseCollection.Name)]
public sealed class EditStudentTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private LoggedPersonal _personal = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _personal = await _factory.LoginPersonalAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static string Edit(Guid id) => $"/personal/alunos/{id}/editar";

    /// <summary>Abre a edição e devolve o antiforgery e a versão (xmin) que a página levou no campo oculto.</summary>
    private async Task<(string Token, string Version, string Html)> OpenAsync(Guid id)
    {
        var page = await _personal.Client.GetAsync(Edit(id));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        var version = System.Text.RegularExpressions.Regex.Match(html, "name=\"Input.Version\" value=\"(\\d+)\"").Groups[1].Value;
        Assert.NotEmpty(version);
        return (AuthTestHelpers.AntiforgeryToken(html), version, WebUtility.HtmlDecode(html));
    }

    private Task<HttpResponseMessage> PostAsync(Guid id, string token, Dictionary<string, string> fields)
    {
        var form = new Dictionary<string, string>(fields)
        {
            ["_handler"] = "editar-aluno",
            ["__RequestVerificationToken"] = token
        };
        return _personal.Client.PostAsync(Edit(id), new FormUrlEncodedContent(form));
    }

    private static Dictionary<string, string> Fields(string version, string? email = null) => new()
    {
        ["Input.Name"] = "Mariana Lopes Silva",
        ["Input.Email"] = email ?? "",
        ["Input.Phone"] = "(21) 99876-5432",
        ["Input.LessonPrice"] = "95,50",
        ["Input.BillingDueDay"] = "5",
        ["Input.Version"] = version
    };

    [Fact]
    public async Task Get_PrefillsCurrentDataInBrazilianFormat()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id, phone: "11987654321");

        var (_, _, html) = await OpenAsync(invite.PersonalStudentId);

        Assert.Contains("value=\"Mariana Lopes\"", html);
        Assert.Contains($"value=\"{invite.Email}\"", html);
        Assert.Contains("value=\"(11) 98765-4321\"", html);
        Assert.Contains("value=\"80,00\"", html);
        Assert.Contains("Se mudar o e-mail, o convite atual é cancelado e você envia um novo link.", html);
        Assert.Contains("O novo valor vale para as aulas registradas a partir de agora.", html);
        Assert.Contains("Salvar alterações", html);
    }

    [Fact]
    public async Task Save_UpdatesData_KeepsInvite_AndAuditsOnlyFieldNames()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        var (token, version, _) = await OpenAsync(invite.PersonalStudentId);

        var response = await PostAsync(invite.PersonalStudentId, token, Fields(version, invite.Email));

        Assert.Equal($"/personal/alunos/{invite.PersonalStudentId}?aviso=salvo", response.RedirectTarget());
        var link = await _factory.LinkAsync(invite.PersonalStudentId);
        Assert.Equal("Mariana Lopes Silva", link.StudentName);
        Assert.Equal(invite.Email, link.StudentEmail);
        Assert.Equal("+5521998765432", link.StudentPhone);
        Assert.Equal(95.50m, link.LessonPrice);
        Assert.Equal(5, link.BillingDueDay);

        var only = Assert.Single(await _factory.InvitesAsync(invite.PersonalStudentId));
        Assert.Null(only.RevokedAtUtc);
        Assert.Contains("Personal Teste convidou você", await _factory.InvitePageTextAsync(invite.Token));

        var audit = await _factory.WithDbAsync(db => db.AuditLogs.SingleAsync(
            a => a.EntityId == invite.PersonalStudentId && a.Action == "StudentUpdated"));
        using var metadata = JsonDocument.Parse(audit.Metadata!);
        Assert.Equal(["BillingDueDay", "LessonPrice", "StudentName", "StudentPhone"],
            metadata.RootElement.GetProperty("changed").EnumerateArray().Select(e => e.GetString()).Order());
        Assert.Equal(80m, metadata.RootElement.GetProperty("lessonPrice").GetProperty("from").GetDecimal());
        Assert.Equal(95.50m, metadata.RootElement.GetProperty("lessonPrice").GetProperty("to").GetDecimal());
        Assert.DoesNotContain("Mariana", audit.Metadata);
        Assert.DoesNotContain("99876", audit.Metadata);
        Assert.DoesNotContain(invite.Email, audit.Metadata);

        var details = await (await _personal.Client.GetAsync(response.RedirectTarget())).TextAsync();
        Assert.Contains("Alterações salvas.", details);
        Assert.Contains("R$ 95,50", details);
    }

    [Fact]
    public async Task Save_NewPrice_DoesNotChangeLessonsAlreadyRegistered()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.ActivateAsync(invite.PersonalStudentId);
        var link = await _factory.LinkAsync(invite.PersonalStudentId);
        var lesson = await _factory.WithDbAsync(db => db.AddLessonAsync(link));
        var (token, version, _) = await OpenAsync(invite.PersonalStudentId);

        await PostAsync(invite.PersonalStudentId, token, Fields(version));

        Assert.Equal(95.50m, (await _factory.LinkAsync(invite.PersonalStudentId)).LessonPrice);
        Assert.Equal(80m, await _factory.WithDbAsync(db => db.Lessons.Where(l => l.Id == lesson.Id).Select(l => l.UnitPrice).SingleAsync()));
    }

    [Fact]
    public async Task Save_OnActiveStudent_IgnoresForgedEmail()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.ActivateAsync(invite.PersonalStudentId);
        var (token, version, html) = await OpenAsync(invite.PersonalStudentId);
        Assert.Contains("É o login do aluno e só ele pode trocar.", html);
        Assert.Contains("readonly", html);
        Assert.DoesNotContain("name=\"Input.Email\"", html);

        var response = await PostAsync(invite.PersonalStudentId, token, Fields(version, "forjado@example.test"));

        Assert.Equal($"/personal/alunos/{invite.PersonalStudentId}?aviso=salvo", response.RedirectTarget());
        var link = await _factory.LinkAsync(invite.PersonalStudentId);
        Assert.Equal(invite.Email, link.StudentEmail);
        Assert.Equal("Mariana Lopes Silva", link.StudentName);
        Assert.Equal(PersonalStudentStatus.Active, link.Status);
    }

    [Fact]
    public async Task Save_NewEmailWhileInvited_RevokesOldLinkAndGoesToNewLink()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        var (token, version, _) = await OpenAsync(invite.PersonalStudentId);
        var newEmail = $"Nova-{Guid.NewGuid():N}@Example.test";

        var response = await PostAsync(invite.PersonalStudentId, token, Fields(version, newEmail));

        Assert.Equal($"/personal/alunos/{invite.PersonalStudentId}/convite-criado", response.RedirectTarget());
        var page = await (await _personal.Client.GetAsync(response.RedirectTarget())).TextAsync();
        var newToken = StudentTestHelpers.InviteTokenIn(page)!;
        Assert.Contains("Novo link para Mariana Lopes Silva", page);
        Assert.Equal(newEmail.ToLowerInvariant(), (await _factory.LinkAsync(invite.PersonalStudentId)).StudentEmail);
        Assert.Contains("Convite não encontrado", await _factory.InvitePageTextAsync(invite.Token));
        // O novo link abre o convite (o e-mail aparece depois do código do WhatsApp, D-057).
        Assert.Contains("Enviar código pelo WhatsApp", await _factory.InvitePageTextAsync(newToken));
        Assert.Equal(1, (await _factory.InvitesAsync(invite.PersonalStudentId)).Count(i => i.RevokedAtUtc is null));
    }

    [Fact]
    public async Task Save_EmailOfAnotherOwnStudent_IsRejectedWithCreateMessage()
    {
        var first = await _factory.CreateInviteAsync(_personal.Account.Id);
        var second = await _factory.CreateInviteAsync(_personal.Account.Id);
        var (token, version, _) = await OpenAsync(second.PersonalStudentId);

        var response = await PostAsync(second.PersonalStudentId, token, Fields(version, first.Email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Você já tem um aluno com este e-mail. Abra o cadastro dele para reenviar o convite.", await response.TextAsync());
        Assert.Equal(second.Email, (await _factory.LinkAsync(second.PersonalStudentId)).StudentEmail);
        Assert.Single(await _factory.InvitesAsync(second.PersonalStudentId));
    }

    [Fact]
    public async Task Save_WithInvalidFields_ShowsSameErrorsAsCreate_AndSavesNothing()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        var (token, version, _) = await OpenAsync(invite.PersonalStudentId);

        var response = await PostAsync(invite.PersonalStudentId, token, new Dictionary<string, string>
        {
            ["Input.Name"] = " ",
            ["Input.Email"] = "sem-arroba",
            ["Input.Phone"] = "123",
            ["Input.LessonPrice"] = "0",
            ["Input.BillingDueDay"] = "32",
            ["Input.Version"] = version
        });
        var html = await response.TextAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Informe o nome do aluno.", html);
        Assert.Contains("Informe um e-mail válido, como aluno@exemplo.com.", html);
        Assert.Contains("Informe um celular com DDD.", html);
        Assert.Contains("Informe um valor entre 0,01 e 99.999,99.", html);
        Assert.Contains("Escolha o dia do vencimento.", html);
        Assert.Contains("aria-invalid=\"true\"", html);
        Assert.Equal("Mariana Lopes", (await _factory.LinkAsync(invite.PersonalStudentId)).StudentName);
    }

    [Fact]
    public async Task Save_WithStaleVersion_ShowsConflictAndReloadsCurrentData()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        var (token, staleVersion, _) = await OpenAsync(invite.PersonalStudentId);

        // Outra aba salva antes.
        var (_, freshVersion, _) = await OpenAsync(invite.PersonalStudentId);
        var other = Fields(freshVersion, invite.Email);
        other["Input.LessonPrice"] = "120,00";
        Assert.Equal($"/personal/alunos/{invite.PersonalStudentId}?aviso=salvo",
            (await PostAsync(invite.PersonalStudentId, token, other)).RedirectTarget());

        var stale = Fields(staleVersion, invite.Email);
        stale["Input.Name"] = "Nome Da Aba Velha";
        var response = await PostAsync(invite.PersonalStudentId, token, stale);
        var html = await response.TextAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Outra alteração foi salva antes. Recarregamos os dados atuais. Confira e salve de novo.", html);
        Assert.Contains("value=\"120,00\"", html);
        Assert.DoesNotContain("Nome Da Aba Velha", html);
        Assert.DoesNotContain($"name=\"Input.Version\" value=\"{staleVersion}\"", html);
        Assert.Equal(120m, (await _factory.LinkAsync(invite.PersonalStudentId)).LessonPrice);
        Assert.Equal("Mariana Lopes Silva", (await _factory.LinkAsync(invite.PersonalStudentId)).StudentName);
    }

    [Fact]
    public async Task Save_AfterStudentAcceptedMeanwhile_IsAConflict()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        var (token, version, _) = await OpenAsync(invite.PersonalStudentId);
        await _factory.ActivateAsync(invite.PersonalStudentId);

        var response = await PostAsync(invite.PersonalStudentId, token, Fields(version, "outro@example.test"));

        Assert.Contains("Outra alteração foi salva antes.", await response.TextAsync());
        Assert.Equal(invite.Email, (await _factory.LinkAsync(invite.PersonalStudentId)).StudentEmail);
    }

    [Fact]
    public async Task Handler_ParallelEditsWithSameVersion_OnlyOneWins()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        var (_, version, _) = await OpenAsync(invite.PersonalStudentId);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async i =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<EditStudentHandler>();
            return await handler.HandleAsync(new EditStudentCommand(_personal.Account.Id, invite.PersonalStudentId,
                new EditStudentInput
                {
                    Name = $"Nome {i}", Email = invite.Email, Phone = "11987654321", LessonPrice = "80",
                    BillingDueDay = "10", Version = version
                }, null), CancellationToken.None);
        }));

        Assert.Single(results, r => r.Outcome == EditStudentOutcome.Succeeded);
        Assert.Equal(3, results.Count(r => r.Outcome == EditStudentOutcome.Conflict));
        Assert.Single(await _factory.AuditActionsAsync(invite.PersonalStudentId), a => a == "StudentUpdated");
    }
}
