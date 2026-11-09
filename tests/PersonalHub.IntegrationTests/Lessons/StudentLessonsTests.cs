using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Lessons;
using PersonalHub.Web.Features.Lessons.DeleteLesson;
using PersonalHub.Web.Features.Lessons.RespondLesson;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.IntegrationTests.Lessons;

/// <summary>Aulas vistas e respondidas pelo aluno (Fase 3), isolamento entre usuários e corridas com a exclusão.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class StudentLessonsTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private LoggedPersonal _personal = default!;
    private LoggedStudent _student = default!;
    private Guid _linkId;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _personal = await _factory.LoginPersonalAsync();
        await _factory.SetPersonalNameAsync(_personal.Account.Id, "Carlos Lima");
        _linkId = (await _factory.CreateInviteAsync(_personal.Account.Id)).PersonalStudentId;
        _student = await _factory.LoginStudentAsync(await _factory.LinkStudentAsync(_linkId));
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static Task<string> PageAsync(HttpClient client, string path) =>
        client.GetAsync(path).ContinueWith(t => t.Result.TextAsync()).Unwrap();

    private Task<string> StudentPageAsync(string path) => PageAsync(_student.Client, path);

    /// <summary>Botão "Confirmar" da lista. O formulário está sempre na página, então o POST sempre chega ao handler.</summary>
    private static Task<HttpResponseMessage> ConfirmFromListAsync(HttpClient client, Guid lessonId) =>
        client.PostWithTokenFromAsync("/aluno/aulas", "confirmar",
            new Dictionary<string, string> { ["aula"] = lessonId.ToString() }, tokenPage: "/aluno/aulas");

    private static Task<HttpResponseMessage> ConfirmFromDetailsAsync(HttpClient client, Guid lessonId) =>
        client.PostWithTokenFromAsync($"/aluno/aulas/{lessonId}", "responder", tokenPage: "/aluno/aulas");

    private static Task<HttpResponseMessage> RejectAsync(HttpClient client, Guid lessonId, string reason, string? suggestion = null)
    {
        var fields = new Dictionary<string, string> { ["motivo"] = reason };
        if (suggestion is not null)
        {
            fields["sugestao"] = suggestion;
        }

        return client.PostWithTokenFromAsync($"/aluno/aulas/{lessonId}/recusar", "recusar", fields, tokenPage: "/aluno/aulas");
    }

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private async Task AssertUntouchedAsync(Guid lessonId)
    {
        var lesson = await _factory.LessonAsync(lessonId);
        Assert.Equal(LessonStatus.Pending, lesson.Status);
        Assert.Null(lesson.RespondedAtUtc);
        Assert.Null(lesson.RejectionReason);
        Assert.Null(lesson.DeletedAtUtc);
        Assert.Empty(await _factory.LessonAuditAsync(lessonId));
    }

    // ---------------------------------------------------------------- Isolamento

    [Fact]
    public async Task OtherStudent_CannotSeeOrAnswerTheLesson()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);
        // Outro aluno, com vínculo próprio com outro personal (vínculo alheio ao da aula).
        var otherPersonal = await _factory.CreateAccountAsync(AppRoles.Personal);
        var otherLink = (await _factory.CreateInviteAsync(otherPersonal.Id, name: "Bruno Souza")).PersonalStudentId;
        var intruder = await _factory.LoginStudentAsync(await _factory.LinkStudentAsync(otherLink));

        var list = await PageAsync(intruder.Client, "/aluno/aulas");
        Assert.DoesNotContain($"/aluno/aulas/{lesson.Id}", list);
        Assert.DoesNotContain("Carlos Lima", list);
        Assert.Contains("Tudo em dia", list);

        foreach (var path in new[] { $"/aluno/aulas/{lesson.Id}", $"/aluno/aulas/{lesson.Id}/recusar" })
        {
            var page = await intruder.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
            Assert.DoesNotContain("Carlos Lima", await page.TextAsync());
        }

        // Mesma resposta de uma aula inexistente: não revela que a aula existe.
        Assert.Equal("/aluno/aulas?aviso=indisponivel", (await ConfirmFromListAsync(intruder.Client, lesson.Id)).RedirectTarget());
        Assert.Equal("/aluno/aulas?aviso=indisponivel", (await ConfirmFromListAsync(intruder.Client, Guid.NewGuid())).RedirectTarget());
        Assert.Equal("/aluno/aulas?aviso=indisponivel", (await ConfirmFromDetailsAsync(intruder.Client, lesson.Id)).RedirectTarget());
        Assert.Equal("/aluno/aulas?aviso=indisponivel", (await RejectAsync(intruder.Client, lesson.Id, "Não fiz")).RedirectTarget());

        await AssertUntouchedAsync(lesson.Id);
    }

    [Fact]
    public async Task Handler_WithAnotherUsersId_ReturnsNotFound()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        var outcomes = await InScopeAsync(async sp =>
        {
            var handler = sp.GetRequiredService<RespondLessonHandler>();
            // O personal da aula também não responde por ela: a resposta é do aluno do vínculo.
            return new[]
            {
                await handler.ApproveAsync(_personal.Account.Id, lesson.Id, null, CancellationToken.None),
                await handler.RejectAsync(Guid.NewGuid(), lesson.Id, "x", null, CancellationToken.None)
            };
        });

        Assert.All(outcomes, o => Assert.Equal(RespondLessonOutcome.NotFound, o));
        await AssertUntouchedAsync(lesson.Id);
    }

    [Fact]
    public async Task OtherPersonal_CannotSeeOrDeleteTheLesson()
    {
        var lesson = await _factory.AddLessonAsync(_linkId, LessonTestHelpers.Utc(LessonTestHelpers.Yesterday, 6, 15));
        var intruder = await _factory.LoginPersonalAsync();

        foreach (var path in new[] { $"/personal/aulas/{lesson.Id}", $"/personal/aulas/{lesson.Id}/excluir" })
        {
            var page = await intruder.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
            Assert.DoesNotContain("Mariana", await page.TextAsync());
        }

        var list = await PageAsync(intruder.Client, $"/personal/aulas?mes={LessonTestHelpers.MonthOf(LessonTestHelpers.Yesterday)}");
        Assert.DoesNotContain(">06:15<", list);
        Assert.DoesNotContain("Mariana", list);

        var outcome = await InScopeAsync(sp => sp.GetRequiredService<DeleteLessonHandler>()
            .HandleAsync(intruder.Account.Id, lesson.Id, "Invasão", null, CancellationToken.None));
        Assert.Equal(DeleteLessonOutcome.NotFound, outcome);
        await AssertUntouchedAsync(lesson.Id);
    }

    [Fact]
    public async Task StudentWithTwoPersonals_SeesLessonsOfBoth()
    {
        var second = await _factory.CreateAccountAsync(AppRoles.Personal);
        await _factory.SetPersonalNameAsync(second.Id, "Ana Prado");
        var secondLink = (await _factory.CreateInviteAsync(second.Id)).PersonalStudentId;
        await _factory.LinkStudentAsync(secondLink, _student.Account);
        var day = LessonTestHelpers.Yesterday;
        await _factory.AddLessonAsync(_linkId, LessonTestHelpers.Utc(day, 7));
        await _factory.AddLessonAsync(secondLink, LessonTestHelpers.Utc(day, 18));
        await _factory.AddLessonAsync(secondLink, LessonTestHelpers.Utc(day.AddDays(-1), 18), LessonStatus.Approved);

        var html = await StudentPageAsync("/aluno/aulas");

        Assert.Contains("Aulas com Ana Prado e Carlos Lima", html);
        Assert.Contains("Carlos Lima · 60 min · R$ 80,00", html);
        Assert.Contains("Ana Prado · 60 min · R$ 80,00", html);
        // Pendentes mais recentes primeiro, de qualquer personal.
        Assert.True(html.IndexOf("às 18:00", StringComparison.Ordinal) < html.IndexOf("às 07:00", StringComparison.Ordinal));
        Assert.Contains(">Confirmada<", html);

        Assert.Contains("Aulas para confirmar", await StudentPageAsync("/aluno"));
        Assert.Contains(">2<", await StudentPageAsync("/aluno"));
    }

    // ---------------------------------------------------------------- Confirmar e recusar

    [Fact]
    public async Task List_EmptyState()
    {
        var html = await StudentPageAsync("/aluno/aulas");

        Assert.Contains("Tudo em dia", html);
        Assert.Contains("As aulas que o seu personal registrar aparecem aqui.", html);
        Assert.Contains("Tudo em dia. Nenhuma aula esperando sua confirmação.", await StudentPageAsync("/aluno"));
    }

    [Fact]
    public async Task Details_Pending_ShowsDataNotesAndActions()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);
        await _factory.WithDbAsync(db => db.Lessons.Where(l => l.Id == lesson.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Notes, "Treino de costas")));

        var html = await StudentPageAsync($"/aluno/aulas/{lesson.Id}");

        Assert.Contains(">Aguardando<", html);
        Assert.Contains("Com Carlos Lima", html);
        Assert.Contains("R$ 80,00", html);
        Assert.Contains("60 min", html);
        Assert.Contains("Treino de costas", html);
        Assert.Contains("Confirmar aula", html);
        Assert.Contains($"href=\"/aluno/aulas/{lesson.Id}/recusar\"", html);
    }

    [Fact]
    public async Task ConfirmFromList_ApprovesAndAudits()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        var response = await ConfirmFromListAsync(_student.Client, lesson.Id);

        Assert.Equal("/aluno/aulas?aviso=confirmada", response.RedirectTarget());
        var saved = await _factory.LessonAsync(lesson.Id);
        Assert.Equal(LessonStatus.Approved, saved.Status);
        Assert.Equal(_student.Account.Id, saved.RespondedByUserId);
        Assert.NotNull(saved.RespondedAtUtc);
        Assert.Equal(["LessonApproved"], await _factory.LessonAuditAsync(lesson.Id));

        var list = await StudentPageAsync(response.RedirectTarget());
        Assert.Contains("Aula confirmada.", list);
        Assert.Contains("Tudo em dia", list);
        Assert.Contains(">Confirmada<", list);

        var details = await StudentPageAsync($"/aluno/aulas/{lesson.Id}");
        Assert.Contains("Você confirmou em", details);
        Assert.DoesNotContain("Confirmar aula", details);
        Assert.Contains("Confirmada por Mariana", await PageAsync(_personal.Client, $"/personal/aulas/{lesson.Id}"));
    }

    [Fact]
    public async Task ConfirmFromDetails_Approves()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        var response = await ConfirmFromDetailsAsync(_student.Client, lesson.Id);

        Assert.Equal("/aluno/aulas?aviso=confirmada", response.RedirectTarget());
        Assert.Equal(LessonStatus.Approved, (await _factory.LessonAsync(lesson.Id)).Status);
    }

    [Fact]
    public async Task Reject_WithReason_RejectsAndPersonalSeesTheReason()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);
        var page = await StudentPageAsync($"/aluno/aulas/{lesson.Id}/recusar");
        Assert.Contains("Carlos Lima vê o motivo", page);
        Assert.Contains("Data ou horário errado", page);

        var response = await RejectAsync(_student.Client, lesson.Id, "  Faltei nesse dia  ");

        Assert.Equal("/aluno/aulas?aviso=recusada", response.RedirectTarget());
        var saved = await _factory.LessonAsync(lesson.Id);
        Assert.Equal(LessonStatus.Rejected, saved.Status);
        Assert.Equal("Faltei nesse dia", saved.RejectionReason);
        Assert.Equal(_student.Account.Id, saved.RespondedByUserId);
        Assert.Equal(["LessonRejected"], await _factory.LessonAuditAsync(lesson.Id));
        var audit = await _factory.WithDbAsync(db => db.AuditLogs.SingleAsync(a => a.EntityId == lesson.Id));
        Assert.Null(audit.Metadata);

        Assert.Contains("Aula recusada. O personal vê o motivo.", await StudentPageAsync(response.RedirectTarget()));
        Assert.Contains("Motivo: Faltei nesse dia", await StudentPageAsync($"/aluno/aulas/{lesson.Id}"));
        Assert.Contains("Motivo: Faltei nesse dia", await PageAsync(_personal.Client, $"/personal/aulas/{lesson.Id}"));
    }

    [Theory]
    [InlineData("", "Escreva o motivo da recusa.")]
    [InlineData("   ", "Escreva o motivo da recusa.")]
    [InlineData("LONG", "O motivo pode ter até 500 caracteres.")]
    public async Task Reject_InvalidReason_ShowsErrorAndKeepsPending(string reason, string message)
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        var response = await RejectAsync(_student.Client, lesson.Id, reason == "LONG" ? new string('a', 501) : reason);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, await response.TextAsync());
        await AssertUntouchedAsync(lesson.Id);
    }

    [Fact]
    public async Task Reject_ReasonWith500Characters_IsAccepted()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        var response = await RejectAsync(_student.Client, lesson.Id, new string('a', 500));

        Assert.Equal("/aluno/aulas?aviso=recusada", response.RedirectTarget());
        Assert.Equal(500, (await _factory.LessonAsync(lesson.Id)).RejectionReason!.Length);
    }

    [Fact]
    public async Task Reject_Suggestion_OnlyFillsTheReason()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        var response = await RejectAsync(_student.Client, lesson.Id, "", suggestion: "Valor errado");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(">Valor errado</textarea>", await response.TextAsync());
        await AssertUntouchedAsync(lesson.Id);
    }

    [Fact]
    public async Task Reject_UnknownSuggestion_IsTreatedAsSubmit()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        // Valor forjado fora da lista: não preenche nada; vale o motivo digitado.
        var response = await RejectAsync(_student.Client, lesson.Id, "Não fiz", suggestion: "<script>");

        Assert.Equal("/aluno/aulas?aviso=recusada", response.RedirectTarget());
        Assert.Equal("Não fiz", (await _factory.LessonAsync(lesson.Id)).RejectionReason);
    }

    // ---------------------------------------------------------------- Resposta única

    [Fact]
    public async Task SecondAnswer_FailsWithFriendlyMessage_AndKeepsTheFirst()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);
        await ConfirmFromListAsync(_student.Client, lesson.Id);

        var again = await ConfirmFromListAsync(_student.Client, lesson.Id);
        Assert.Equal("/aluno/aulas?aviso=ja-respondida", again.RedirectTarget());
        Assert.Contains("Esta aula já tinha sido respondida. A resposta não muda.", await StudentPageAsync(again.RedirectTarget()));

        var fromDetails = await ConfirmFromDetailsAsync(_student.Client, lesson.Id);
        Assert.Equal($"/aluno/aulas/{lesson.Id}?aviso=ja-respondida", fromDetails.RedirectTarget());
        Assert.Contains("Esta aula já tinha sido respondida.", await StudentPageAsync(fromDetails.RedirectTarget()));

        // Recusa em outra aba depois da confirmação: nem o motivo vazio importa, a aula já foi respondida.
        var reject = await RejectAsync(_student.Client, lesson.Id, "");
        Assert.Equal($"/aluno/aulas/{lesson.Id}?aviso=ja-respondida", reject.RedirectTarget());

        // A página de recusa de uma aula respondida volta para o detalhe.
        Assert.Equal($"/aluno/aulas/{lesson.Id}", (await _student.Client.GetAsync($"/aluno/aulas/{lesson.Id}/recusar")).RedirectTarget());

        var saved = await _factory.LessonAsync(lesson.Id);
        Assert.Equal(LessonStatus.Approved, saved.Status);
        Assert.Null(saved.RejectionReason);
        Assert.Equal(["LessonApproved"], await _factory.LessonAuditAsync(lesson.Id));
    }

    [Fact]
    public async Task Handler_ParallelAnswers_OnlyOneWins()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => InScopeAsync(sp =>
        {
            var handler = sp.GetRequiredService<RespondLessonHandler>();
            return i % 2 == 0
                ? handler.ApproveAsync(_student.Account.Id, lesson.Id, null, CancellationToken.None)
                : handler.RejectAsync(_student.Account.Id, lesson.Id, "Não fiz", null, CancellationToken.None);
        })));

        Assert.Single(results, r => r == RespondLessonOutcome.Succeeded);
        Assert.Equal(3, results.Count(r => r == RespondLessonOutcome.AlreadyResponded));
        Assert.Single(await _factory.LessonAuditAsync(lesson.Id));
    }

    // ---------------------------------------------------------------- Exclusão vista pelo aluno

    [Fact]
    public async Task DeletedPending_IsHiddenFromStudent()
    {
        var lesson = await _factory.AddLessonAsync(_linkId, deleted: true);

        var list = await StudentPageAsync("/aluno/aulas");
        Assert.DoesNotContain(lesson.Id.ToString(), list);
        Assert.DoesNotContain("Excluída", list);
        Assert.Contains("Tudo em dia", list);
        Assert.Contains("Tudo em dia", await StudentPageAsync("/aluno"));

        Assert.Equal(HttpStatusCode.NotFound, (await _student.Client.GetAsync($"/aluno/aulas/{lesson.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _student.Client.GetAsync($"/aluno/aulas/{lesson.Id}/recusar")).StatusCode);
        Assert.Equal("/aluno/aulas?aviso=indisponivel", (await ConfirmFromListAsync(_student.Client, lesson.Id)).RedirectTarget());
    }

    [Theory]
    [InlineData(LessonStatus.Approved)]
    [InlineData(LessonStatus.Rejected)]
    public async Task DeletedResponded_AppearsAsDeletedWithReason(LessonStatus status)
    {
        var lesson = await _factory.AddLessonAsync(_linkId, status: status, deleted: true);

        var list = await StudentPageAsync("/aluno/aulas");
        Assert.Contains($"/aluno/aulas/{lesson.Id}", list);
        Assert.Contains(">Excluída<", list);
        Assert.Contains("Excluída pelo personal", list);

        var details = await StudentPageAsync($"/aluno/aulas/{lesson.Id}");
        Assert.Contains(">Excluída<", details);
        Assert.Contains("Excluída por Carlos Lima em", details);
        Assert.Contains("Motivo: Registrei errado", details);
        Assert.Contains("Esta aula não será cobrada.", details);
        Assert.DoesNotContain("Confirmar aula", details);
    }

    [Fact]
    public async Task AnswerAfterDeletion_ShowsUnavailable()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);
        // O aluno abriu a aula; o personal excluiu antes do envio.
        Assert.Contains("Confirmar aula", await StudentPageAsync($"/aluno/aulas/{lesson.Id}"));
        await InScopeAsync(sp => sp.GetRequiredService<DeleteLessonHandler>()
            .HandleAsync(_personal.Account.Id, lesson.Id, null, null, CancellationToken.None));

        var confirm = await ConfirmFromDetailsAsync(_student.Client, lesson.Id);
        var reject = await RejectAsync(_student.Client, lesson.Id, "Não fiz");

        Assert.Equal("/aluno/aulas?aviso=indisponivel", confirm.RedirectTarget());
        Assert.Equal("/aluno/aulas?aviso=indisponivel", reject.RedirectTarget());
        Assert.Contains("Esta aula não está mais disponível para resposta.", await StudentPageAsync(confirm.RedirectTarget()));
        var saved = await _factory.LessonAsync(lesson.Id);
        Assert.Equal(LessonStatus.Pending, saved.Status);
        Assert.NotNull(saved.DeletedAtUtc);
    }

    // ---------------------------------------------------------------- Corrida resposta × exclusão

    [Fact]
    public async Task Race_DeleteHoldsTheLock_AnswerWaitsAndSeesDeleted()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var locked = await db.LockForPersonalAsync(_personal.Account.Id, lesson.Id, CancellationToken.None);
            Assert.NotNull(locked);

            var answer = InScopeAsync(sp => sp.GetRequiredService<RespondLessonHandler>()
                .ApproveAsync(_student.Account.Id, lesson.Id, null, CancellationToken.None));
            // A resposta fica esperando a linha travada.
            Assert.NotSame(answer, await Task.WhenAny(answer, Task.Delay(TimeSpan.FromMilliseconds(500))));

            locked.Delete(_personal.Account.Id, null, DateTime.UtcNow);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            Assert.Equal(RespondLessonOutcome.Deleted, await answer);
        }

        var saved = await _factory.LessonAsync(lesson.Id);
        Assert.Equal(LessonStatus.Pending, saved.Status);
        Assert.NotNull(saved.DeletedAtUtc);
    }

    [Fact]
    public async Task Race_AnswerHoldsTheLock_DeleteWithoutReasonWaitsAndAsksForReason()
    {
        var lesson = await _factory.AddLessonAsync(_linkId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var locked = await db.LockForStudentAsync(_student.Account.Id, lesson.Id, CancellationToken.None);
            Assert.NotNull(locked);

            var delete = InScopeAsync(sp => sp.GetRequiredService<DeleteLessonHandler>()
                .HandleAsync(_personal.Account.Id, lesson.Id, null, null, CancellationToken.None));
            Assert.NotSame(delete, await Task.WhenAny(delete, Task.Delay(TimeSpan.FromMilliseconds(500))));

            locked.Approve(_student.Account.Id, DateTime.UtcNow);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            // Pendente quando o personal abriu a confirmação; respondida quando a exclusão pegou a linha (D-042).
            Assert.Equal(DeleteLessonOutcome.ReasonRequired, await delete);
        }

        var saved = await _factory.LessonAsync(lesson.Id);
        Assert.Equal(LessonStatus.Approved, saved.Status);
        Assert.Null(saved.DeletedAtUtc);
    }

    [Fact]
    public async Task Race_ParallelAnswerAndDelete_EndInAConsistentState()
    {
        for (var i = 0; i < 5; i++)
        {
            var lesson = await _factory.AddLessonAsync(_linkId);

            var answer = InScopeAsync(sp => sp.GetRequiredService<RespondLessonHandler>()
                .ApproveAsync(_student.Account.Id, lesson.Id, null, CancellationToken.None));
            var delete = InScopeAsync(sp => sp.GetRequiredService<DeleteLessonHandler>()
                .HandleAsync(_personal.Account.Id, lesson.Id, null, null, CancellationToken.None));
            await Task.WhenAll(answer, delete);
            var answered = await answer;
            var deleted = await delete;

            var saved = await _factory.LessonAsync(lesson.Id);
            if (answered == RespondLessonOutcome.Succeeded)
            {
                Assert.Equal(DeleteLessonOutcome.ReasonRequired, deleted);
                Assert.Equal(LessonStatus.Approved, saved.Status);
                Assert.Null(saved.DeletedAtUtc);
            }
            else
            {
                Assert.Equal(RespondLessonOutcome.Deleted, answered);
                Assert.Equal(DeleteLessonOutcome.Succeeded, deleted);
                Assert.Equal(LessonStatus.Pending, saved.Status);
                Assert.NotNull(saved.DeletedAtUtc);
            }

            Assert.Single(await _factory.LessonAuditAsync(lesson.Id));
        }
    }

    // ---------------------------------------------------------------- Vínculo convidado

    [Fact]
    public async Task InvitedLink_ReceivesLesson_AndStudentSeesItAfterAcceptingTheInvite()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id, name: "Paula Reis");
        Assert.Equal(PersonalStudentStatus.Invited, (await _factory.LinkAsync(invite.PersonalStudentId)).Status);

        var registered = await _personal.Client.PostRegisterLessonAsync(
            LessonTestHelpers.LessonFields(invite.PersonalStudentId, time: "08:00"));
        Assert.Contains("aviso=registrada", registered.RedirectTarget());
        var lesson = Assert.Single(await _factory.LessonsOfAsync(invite.PersonalStudentId));
        Assert.Equal(LessonStatus.Pending, lesson.Status);
        Assert.Contains("Aguardando Paula confirmar", await PageAsync(_personal.Client, $"/personal/aulas/{lesson.Id}"));

        var browser = _factory.CreateBrowser();
        var accepted = await browser.PostFormAsync($"/convite/{invite.Token}", "nova-conta", new Dictionary<string, string>
        {
            ["New.Name"] = "Paula Reis",
            ["New.Password"] = "Aluna-Forte1",
            ["New.ConfirmPassword"] = "Aluna-Forte1",
            ["New.AcceptTerms"] = "true"
        });
        Assert.Equal("/aluno", accepted.RedirectTarget());

        Assert.Contains("Aula para confirmar", await PageAsync(browser, "/aluno"));
        var list = await PageAsync(browser, "/aluno/aulas");
        Assert.Contains($"/aluno/aulas/{lesson.Id}", list);
        Assert.Contains("às 08:00", list);
        Assert.Contains("Carlos Lima · 60 min · R$ 80,00", list);

        Assert.Equal("/aluno/aulas?aviso=confirmada", (await ConfirmFromListAsync(browser, lesson.Id)).RedirectTarget());
        var saved = await _factory.LessonAsync(lesson.Id);
        Assert.Equal(LessonStatus.Approved, saved.Status);
        Assert.Equal((await _factory.LinkAsync(invite.PersonalStudentId)).StudentUserId, saved.RespondedByUserId);
    }
}
