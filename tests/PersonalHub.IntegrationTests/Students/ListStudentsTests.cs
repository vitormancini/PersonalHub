using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Domain;

namespace PersonalHub.IntegrationTests.Students;

[Collection(DatabaseCollection.Name)]
public sealed class ListStudentsTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private LoggedPersonal _personal = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _personal = await _factory.LoginPersonalAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task List_WithoutStudents_ShowsEmptyState()
    {
        var page = await _personal.Client.GetAsync("/personal/alunos");
        var html = await page.TextAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Nenhum aluno ainda", html);
        Assert.Contains("Cadastre seu primeiro aluno e envie o convite pelo WhatsApp.", html);
        Assert.Contains("href=\"/personal/alunos/novo\"", html);
        Assert.DoesNotContain("Buscar aluno", html);
    }

    [Fact]
    public async Task List_OpensOnActive_InAlphabeticalOrder_WithCountsAndOnlyOwnStudents()
    {
        var zeca = await _factory.CreateInviteAsync(_personal.Account.Id, name: "Zeca Prado");
        var alvaro = await _factory.CreateInviteAsync(_personal.Account.Id, name: "Álvaro Dias");
        var bruna = await _factory.CreateInviteAsync(_personal.Account.Id, name: "bruna Costa");
        await _factory.CreateInviteAsync(_personal.Account.Id, name: "Carla Convidada");
        var inactive = await _factory.CreateInviteAsync(_personal.Account.Id, name: "Davi Inativo");
        await _factory.ActivateAsync(zeca.PersonalStudentId);
        await _factory.ActivateAsync(alvaro.PersonalStudentId);
        await _factory.ActivateAsync(bruna.PersonalStudentId);
        await _factory.SetInactiveAsync(inactive.PersonalStudentId, DateTime.UtcNow);

        var other = await _factory.LoginPersonalAsync();
        var foreign = await _factory.CreateInviteAsync(other.Account.Id, name: "Aluno De Outro");
        await _factory.ActivateAsync(foreign.PersonalStudentId);

        var html = await (await _personal.Client.GetAsync("/personal/alunos")).TextAsync();

        Assert.Matches("aria-current=\"page\">\\s*Ativos <span class=\"ph-filter-count\">3</span>", html);
        Assert.Contains("Convidados <span class=\"ph-filter-count\">1</span>", html);
        Assert.Contains("Inativos <span class=\"ph-filter-count\">1</span>", html);
        Assert.Contains("Todos <span class=\"ph-filter-count\">5</span>", html);
        Assert.DoesNotContain("Aluno De Outro", html);
        Assert.DoesNotContain(foreign.PersonalStudentId.ToString(), html);
        Assert.DoesNotContain("Carla Convidada", html);

        // Ordem do português: acento e minúscula não jogam o nome para o fim.
        var alvaroAt = html.IndexOf("Álvaro Dias", StringComparison.Ordinal);
        var brunaAt = html.IndexOf("bruna Costa", StringComparison.Ordinal);
        var zecaAt = html.IndexOf("Zeca Prado", StringComparison.Ordinal);
        Assert.True(alvaroAt >= 0 && alvaroAt < brunaAt && brunaAt < zecaAt, "Lista fora da ordem alfabética.");

        Assert.Contains($"href=\"/personal/alunos/{alvaro.PersonalStudentId}\"", html);
        Assert.Contains("R$ 80,00 por aula · vence dia 10", html);
        Assert.Contains("ph-pill-success\">Ativo<", html);
        Assert.Contains(">ÁD<", html);
    }

    [Fact]
    public async Task List_InvitedFilter_DescribesEachInviteState()
    {
        var pending = await _factory.CreateInviteAsync(_personal.Account.Id, name: "Ana Pendente");
        var expired = await _factory.CreateInviteAsync(_personal.Account.Id, name: "Beto Expirado");
        var cancelled = await _factory.CreateInviteAsync(_personal.Account.Id, name: "Caio Cancelado");
        await _factory.SetInviteAsync(expired.PersonalStudentId, expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        await _factory.SetInviteAsync(cancelled.PersonalStudentId, revokedAtUtc: DateTime.UtcNow);
        var expiresAt = (await _factory.InvitesAsync(pending.PersonalStudentId)).Single().ExpiresAtUtc;

        var html = await (await _personal.Client.GetAsync("/personal/alunos?situacao=convidados")).TextAsync();

        Assert.Contains($"Convite expira em {SaoPaulo.ToLocalDate(expiresAt):dd/MM}", html);
        Assert.Contains("Convite expirado", html);
        Assert.Contains("Convite cancelado", html);
        Assert.Contains("ph-pill-pending\">Convidado<", html);
        Assert.Contains("value=\"convidados\"", html);
    }

    [Fact]
    public async Task List_InactiveFilter_ShowsSinceDate()
    {
        var link = await _factory.CreateInviteAsync(_personal.Account.Id, name: "Davi Inativo");
        await _factory.SetInactiveAsync(link.PersonalStudentId, new DateTime(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc));

        var html = await (await _personal.Client.GetAsync("/personal/alunos?situacao=inativos")).TextAsync();

        Assert.Contains("Davi Inativo", html);
        Assert.Contains("Inativo desde 01/10/2026", html);
        Assert.Contains("ph-pill-muted\">Inativo<", html);
    }

    [Fact]
    public async Task List_Search_IsCaseInsensitive_KeepsFilter_AndTreatsWildcardsAsText()
    {
        await _factory.CreateInviteAsync(_personal.Account.Id, name: "Mariana Lopes");
        await _factory.CreateInviteAsync(_personal.Account.Id, name: "Pedro Alves");

        var found = await (await _personal.Client.GetAsync("/personal/alunos?situacao=todos&q=MARI")).TextAsync();
        Assert.Contains("Mariana Lopes", found);
        Assert.DoesNotContain("Pedro Alves", found);
        Assert.Contains("Todos <span class=\"ph-filter-count\">1</span>", found);
        Assert.Contains("href=\"/personal/alunos?situacao=convidados&q=MARI\"", found);

        var wildcard = await (await _personal.Client.GetAsync("/personal/alunos?situacao=todos&q=%25")).TextAsync();
        Assert.Contains("Nenhum aluno encontrado para \"%\".", wildcard);
        Assert.DoesNotContain("Mariana Lopes", wildcard);
    }

    [Fact]
    public async Task List_FilterWithoutResults_ShowsFilterMessage_AndUnknownFilterFallsBackToActive()
    {
        await _factory.CreateInviteAsync(_personal.Account.Id);

        var empty = await (await _personal.Client.GetAsync("/personal/alunos")).TextAsync();
        Assert.Contains("Nenhum aluno ativo.", empty);

        var unknown = await (await _personal.Client.GetAsync("/personal/alunos?situacao=1")).TextAsync();
        Assert.Contains("Nenhum aluno ativo.", unknown);
    }
}
