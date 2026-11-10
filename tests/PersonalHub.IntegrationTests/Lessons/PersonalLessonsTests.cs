using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Features.Lessons;
using PersonalHub.Web.Features.Lessons.DeleteLesson;

namespace PersonalHub.IntegrationTests.Lessons;

/// <summary>Lista, detalhe e exclusão de aulas pelo personal.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PersonalLessonsTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private LoggedPersonal _personal = default!;
    private Guid _linkId;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _personal = await _factory.LoginPersonalAsync();
        _linkId = (await _factory.CreateInviteAsync(_personal.Account.Id)).PersonalStudentId;
        await _factory.LinkStudentAsync(_linkId);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static string Month(DateOnly day) => LessonTestHelpers.MonthOf(day);

    private Task<string> PageAsync(string path) => _personal.Client.GetAsync(path).ContinueWith(t => t.Result.TextAsync()).Unwrap();

    private Task<HttpResponseMessage> DeleteAsync(Guid lessonId, string reason = "") =>
        _personal.Client.PostFormAsync($"/personal/aulas/{lessonId}/excluir", "excluir-aula",
            new Dictionary<string, string> { ["motivo"] = reason });

    [Fact]
    public async Task List_ShowsMonthGroupedByDayWithCountersAndHidesDeleted()
    {
        var day = LessonTestHelpers.Yesterday;
        await _factory.AddLessonAsync(_linkId, LessonTestHelpers.Utc(day, 7));
        await _factory.AddLessonAsync(_linkId, LessonTestHelpers.Utc(day, 9), LessonStatus.Approved);
        await _factory.AddLessonAsync(_linkId, LessonTestHelpers.Utc(day, 11), LessonStatus.Rejected);
        await _factory.AddLessonAsync(_linkId, LessonTestHelpers.Utc(day, 13), LessonStatus.Approved, deleted: true);

        var html = await PageAsync($"/personal/aulas?mes={Month(day)}");

        Assert.Contains(">Aguardando<", html);
        Assert.Contains(">Confirmada<", html);
        Assert.Contains(">Recusada<", html);
        Assert.DoesNotContain(">Excluída<", html);
        Assert.Contains(">11:00<", html);
        Assert.DoesNotContain(">13:00<", html);
        Assert.Contains("Mostrar excluídas (1)", html);
        // Mais recente primeiro.
        Assert.True(html.IndexOf(">11:00<", StringComparison.Ordinal) < html.IndexOf(">07:00<", StringComparison.Ordinal));
        Assert.Contains(LessonDisplay.Month(new DateOnly(day.Year, day.Month, 1)), html);

        var withDeleted = await PageAsync($"/personal/aulas?mes={Month(day)}&excluidas=1");
        Assert.Contains(">Excluída<", withDeleted);
        Assert.Contains("Ocultar excluídas", withDeleted);
    }

    [Theory]
    [InlineData("aguardando", "07:00", "09:00")]
    [InlineData("confirmadas", "09:00", "07:00")]
    [InlineData("recusadas", "11:00", "09:00")]
    public async Task List_FilterBySituation(string situacao, string shown, string hidden)
    {
        var day = LessonTestHelpers.Yesterday;
        await _factory.AddLessonAsync(_linkId, LessonTestHelpers.Utc(day, 7));
        await _factory.AddLessonAsync(_linkId, LessonTestHelpers.Utc(day, 9), LessonStatus.Approved);
        await _factory.AddLessonAsync(_linkId, LessonTestHelpers.Utc(day, 11), LessonStatus.Rejected);

        var html = await PageAsync($"/personal/aulas?mes={Month(day)}&situacao={situacao}");

        Assert.Contains($">{shown}<", html);
        Assert.DoesNotContain($">{hidden}<", html);
        // O contador ativo leva de volta à lista sem filtro.
        Assert.Contains($"href=\"/personal/aulas?mes={Month(day)}\" class=\"ph-counter ph-counter-{situacao} is-active\"", html);
    }

    [Fact]
    public async Task List_FilterByStudent()
    {
        var other = await _factory.CreateInviteAsync(_personal.Account.Id, name: "Bruno Souza");
        var day = LessonTestHelpers.Yesterday;
        await _factory.AddLessonAsync(_linkId, LessonTestHelpers.Utc(day, 7));
        await _factory.AddLessonAsync(other.PersonalStudentId, LessonTestHelpers.Utc(day, 8));

        var all = await PageAsync($"/personal/aulas?mes={Month(day)}");
        var filtered = await PageAsync($"/personal/aulas?mes={Month(day)}&aluno={other.PersonalStudentId}");

        Assert.Contains("ph-list-title\">Mariana Lopes<", all);
        Assert.Contains("ph-list-title\">Bruno Souza<", all);
        Assert.DoesNotContain("ph-list-title\">Mariana Lopes<", filtered);
        Assert.Contains("ph-list-title\">Bruno Souza<", filtered);
        Assert.Contains($"href=\"/personal/aulas/nova?aluno={other.PersonalStudentId}\"", filtered);
    }

    [Fact]
    public async Task List_EmptyMonth_ShowsEmptyState()
    {
        var html = await PageAsync("/personal/aulas?mes=2020-01");

        Assert.Contains("Janeiro de 2020", html);
        Assert.Contains("Nenhuma aula neste mês", html);
        Assert.Contains("Registre a aula logo depois de dar. O aluno recebe para confirmar.", html);
    }

    [Fact]
    public async Task List_FutureOrInvalidMonth_FallsBackToCurrent()
    {
        var today = SaoPaulo.ToLocalDate(DateTime.UtcNow);
        var current = LessonDisplay.Month(new DateOnly(today.Year, today.Month, 1));

        Assert.Contains(current, await PageAsync("/personal/aulas?mes=2999-01"));
        Assert.Contains(current, await PageAsync("/personal/aulas?mes=abc"));
    }

    [Fact]
    public async Task Details_Pending_ShowsDataAndWaitingHistory()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        var html = await PageAsync($"/personal/aulas/{lesson.Id}");

        Assert.Contains("ph-pill-pending\">Aguardando<", html);
        Assert.Contains("Mariana Lopes", html);
        Assert.Contains("R$ 80,00", html);
        Assert.Contains("60 min", html);
        Assert.Contains("Registrada por você", html);
        Assert.Contains("Aguardando Mariana confirmar", html);
        Assert.Contains("Sem prazo", html);
        Assert.Contains("Excluir aula", html);
    }

    [Fact]
    public async Task Details_Rejected_ShowsReasonAndHowToFix()
    {
        var lesson = await _factory.AddLessonAsync(_linkId, status: LessonStatus.Rejected);

        var html = await PageAsync($"/personal/aulas/{lesson.Id}");

        Assert.Contains("Motivo: Não fiz esta aula", html);
        Assert.Contains("Para corrigir, exclua esta aula e registre outra com os dados certos.", html);
        Assert.Contains("Recusada por Mariana", html);
    }

    [Fact]
    public async Task Delete_Pending_ReasonOptional_HidesFromStudent()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);
        var page = await PageAsync($"/personal/aulas/{lesson.Id}/excluir");
        Assert.Contains("Mariana ainda não respondeu. A aula some da lista e não entra em nenhum demonstrativo.", page);
        Assert.Contains("(opcional)", page);

        var response = await DeleteAsync(lesson.Id);

        Assert.Equal($"/personal/aulas/{lesson.Id}?aviso=excluida", response.RedirectTarget());
        var deleted = await _factory.LessonAsync(lesson.Id);
        Assert.NotNull(deleted.DeletedAtUtc);
        Assert.Equal(_personal.Account.Id, deleted.DeletedByUserId);
        Assert.Null(deleted.DeletionReason);
        Assert.Contains("LessonDeleted", await _factory.LessonAuditAsync(lesson.Id));

        var details = await PageAsync(response.RedirectTarget());
        Assert.Contains("Aula excluída.", details);
        Assert.Contains("Excluída por você em", details);
        Assert.Contains("Não entra em nenhum demonstrativo. Fica no histórico para consulta.", details);
        Assert.DoesNotContain("href=\"/personal/aulas/" + lesson.Id + "/excluir\"", details);
    }

    [Theory]
    [InlineData(LessonStatus.Approved)]
    [InlineData(LessonStatus.Rejected)]
    public async Task Delete_Responded_RequiresReason(LessonStatus status)
    {
        var lesson = await _factory.AddLessonAsync(_linkId, status: status);
        var page = await PageAsync($"/personal/aulas/{lesson.Id}/excluir");
        Assert.Contains("Mariana já respondeu esta aula. Vai ver que a aula foi excluída e o motivo que você escrever.", page);

        var empty = await DeleteAsync(lesson.Id, "   ");
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains("Conte para Mariana por que a aula foi excluída.", await empty.TextAsync());
        Assert.Null((await _factory.LessonAsync(lesson.Id)).DeletedAtUtc);

        var response = await DeleteAsync(lesson.Id, "Registrei no dia errado");

        Assert.Equal($"/personal/aulas/{lesson.Id}?aviso=excluida", response.RedirectTarget());
        var deleted = await _factory.LessonAsync(lesson.Id);
        Assert.Equal("Registrei no dia errado", deleted.DeletionReason);
        Assert.Equal(status, deleted.Status);
        Assert.Contains("Motivo: Registrei no dia errado", await PageAsync($"/personal/aulas/{lesson.Id}"));
    }

    [Fact]
    public async Task Delete_ReasonTooLong_IsRejected()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        var response = await DeleteAsync(lesson.Id, new string('a', 501));

        Assert.Contains("O motivo pode ter até 500 caracteres.", await response.TextAsync());
        Assert.Null((await _factory.LessonAsync(lesson.Id)).DeletedAtUtc);
    }

    [Fact]
    public async Task Delete_AlreadyDeleted_RedirectsToDetails()
    {
        var lesson = await _factory.AddLessonAsync(_linkId, deleted: true);

        var page = await _personal.Client.GetAsync($"/personal/aulas/{lesson.Id}/excluir");

        Assert.Equal($"/personal/aulas/{lesson.Id}", page.RedirectTarget());
    }

    [Fact]
    public async Task Handler_StudentAnsweredWhileConfirmationWasOpen_AsksForReason()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);
        // A página abriu com a aula pendente (motivo opcional); o aluno confirmou antes do envio.
        Assert.Contains("(opcional)", await PageAsync($"/personal/aulas/{lesson.Id}/excluir"));
        var studentUserId = (await _factory.LinkAsync(_linkId)).StudentUserId;
        await _factory.WithDbAsync(db => db.Lessons.Where(l => l.Id == lesson.Id).ExecuteUpdateAsync(s => s
            .SetProperty(l => l.Status, LessonStatus.Approved)
            .SetProperty(l => l.RespondedAtUtc, DateTime.UtcNow)
            .SetProperty(l => l.RespondedByUserId, studentUserId)));

        var response = await DeleteAsync(lesson.Id);

        var html = await response.TextAsync();
        Assert.Contains("Conte para Mariana por que a aula foi excluída.", html);
        Assert.Contains("Mariana já respondeu esta aula.", html);
        Assert.Null((await _factory.LessonAsync(lesson.Id)).DeletedAtUtc);
    }

    [Fact]
    public async Task Handler_ParallelDeletes_DeleteOnce()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<DeleteLessonHandler>();
            return await handler.HandleAsync(_personal.Account.Id, lesson.Id, null, null, CancellationToken.None);
        }));

        Assert.Single(results, r => r == DeleteLessonOutcome.Succeeded);
        Assert.Equal(3, results.Count(r => r == DeleteLessonOutcome.AlreadyDeleted));
        Assert.Single(await _factory.LessonAuditAsync(lesson.Id), a => a == "LessonDeleted");
    }
}
