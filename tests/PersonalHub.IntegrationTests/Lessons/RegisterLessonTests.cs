using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Features.Lessons.RegisterLesson;

namespace PersonalHub.IntegrationTests.Lessons;

[Collection(DatabaseCollection.Name)]
public sealed partial class RegisterLessonTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;
    private LoggedPersonal _personal = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _personal = await _factory.LoginPersonalAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<Guid> ActiveStudentAsync()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _factory.LinkStudentAsync(invite.PersonalStudentId);
        return invite.PersonalStudentId;
    }

    [Fact]
    public async Task Register_ForActiveStudent_CreatesPendingLessonWithLinkPriceAndAudit()
    {
        var linkId = await ActiveStudentAsync();
        var day = LessonTestHelpers.Yesterday;

        var response = await _personal.Client.PostRegisterLessonAsync(
            LessonTestHelpers.LessonFields(linkId, day, "07:30", "45", "  Treino de pernas  "));

        Assert.Equal($"/personal/aulas?mes={LessonTestHelpers.MonthOf(day)}&aviso=registrada", response.RedirectTarget());
        var lesson = Assert.Single(await _factory.LessonsOfAsync(linkId));
        Assert.Equal(LessonStatus.Pending, lesson.Status);
        Assert.Equal(80.00m, lesson.UnitPrice);
        Assert.Equal((short)45, lesson.DurationMinutes);
        Assert.Equal("Treino de pernas", lesson.Notes);
        Assert.Equal(LessonTestHelpers.Utc(day, 7, 30), lesson.OccurredAtUtc);
        Assert.Equal(_personal.Account.Id, lesson.CreatedByUserId);
        Assert.Null(lesson.DeletedAtUtc);

        var audit = await _factory.WithDbAsync(db => db.AuditLogs.SingleAsync(a => a.EntityId == lesson.Id));
        Assert.Equal("LessonRegistered", audit.Action);
        Assert.Equal(_personal.Account.Id, audit.ActorUserId);
        Assert.DoesNotContain("Treino", audit.Metadata);

        var list = await (await _personal.Client.GetAsync(response.RedirectTarget())).TextAsync();
        Assert.Contains("Aula registrada. O aluno recebe para confirmar.", list);
        Assert.Contains("Mariana Lopes", list);
        Assert.Contains("45 min · R$ 80,00", list);
    }

    [Fact]
    public async Task Register_WithoutDurationAndNotes_IsAccepted()
    {
        var linkId = await ActiveStudentAsync();

        await _personal.Client.PostRegisterLessonAsync(LessonTestHelpers.LessonFields(linkId, duration: "", notes: ""));

        var lesson = Assert.Single(await _factory.LessonsOfAsync(linkId));
        Assert.Null(lesson.DurationMinutes);
        Assert.Null(lesson.Notes);
    }

    [Fact]
    public async Task Page_WithStudentInQuery_PreselectsAndShowsPrice()
    {
        var linkId = await ActiveStudentAsync();

        var page = await (await _personal.Client.GetAsync($"/personal/aulas/nova?aluno={linkId}")).TextAsync();

        Assert.Contains($"<option value=\"{linkId}\" data-name=\"Mariana\" data-price=\"R$ 80,00\" data-invited=\"false\" selected>", page);
        Assert.Contains("data-ph-lesson-price>R$ 80,00<", page);
        Assert.Contains("Vem do cadastro de <span data-ph-lesson-name>Mariana</span> e fica fixo nesta aula.", page);
        Assert.Contains("O aluno vê a observação.", page);
    }

    [Fact]
    public async Task Register_ForInvitedStudent_IsAllowedAndStaysPending()
    {
        var invite = await _factory.CreateInviteAsync(_personal.Account.Id);

        var page = await (await _personal.Client.GetAsync($"/personal/aulas/nova?aluno={invite.PersonalStudentId}")).TextAsync();
        Assert.Contains("ainda não aceitou o convite. A aula fica aguardando", page);
        Assert.DoesNotMatch(InvitedNoticeHidden(), page);

        var response = await _personal.Client.PostRegisterLessonAsync(LessonTestHelpers.LessonFields(invite.PersonalStudentId));

        Assert.StartsWith("/personal/aulas?mes=", response.RedirectTarget());
        Assert.Equal(LessonStatus.Pending, Assert.Single(await _factory.LessonsOfAsync(invite.PersonalStudentId)).Status);
    }

    [Fact]
    public async Task Register_ForInactiveStudent_IsBlocked()
    {
        var linkId = await ActiveStudentAsync();
        await _factory.SetInactiveAsync(linkId, DateTime.UtcNow);

        var page = await (await _personal.Client.GetAsync("/personal/aulas/nova")).TextAsync();
        Assert.DoesNotContain($"value=\"{linkId}\"", page);

        var response = await _personal.Client.PostRegisterLessonAsync(LessonTestHelpers.LessonFields(linkId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Este aluno está inativo. Reative o vínculo para registrar aulas.", await response.TextAsync());
        Assert.Empty(await _factory.LessonsOfAsync(linkId));
    }

    [Fact]
    public async Task Register_InFuture_IsRejected()
    {
        var linkId = await ActiveStudentAsync();
        var tomorrow = LessonTestHelpers.Yesterday.AddDays(2);

        var response = await _personal.Client.PostRegisterLessonAsync(LessonTestHelpers.LessonFields(linkId, tomorrow));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("A aula não pode ser no futuro. Registre depois de dar a aula.", await response.TextAsync());
        Assert.Empty(await _factory.LessonsOfAsync(linkId));
    }

    [Fact]
    public async Task Register_FutureRule_UsesSaoPauloTimeZone()
    {
        // 02:30 UTC de 05/11 = 23:30 de 04/11 em Brasília.
        await using var factory = _factory.WithClock(new DateTimeOffset(2026, 11, 5, 2, 30, 0, TimeSpan.Zero));
        var personal = await factory.LoginPersonalAsync();
        var invite = await factory.CreateInviteAsync(personal.Account.Id);

        var page = await (await personal.Client.GetAsync("/personal/aulas/nova")).TextAsync();
        Assert.Contains("max=\"2026-11-04\"", page);
        Assert.Contains("value=\"2026-11-04\"", page);

        // Já é dia 05 em UTC, mas ainda não em Brasília.
        var future = await personal.Client.PostRegisterLessonAsync(
            LessonTestHelpers.LessonFields(invite.PersonalStudentId, new DateOnly(2026, 11, 5), "00:10"));
        Assert.Contains("A aula não pode ser no futuro.", await future.TextAsync());

        var past = await personal.Client.PostRegisterLessonAsync(
            LessonTestHelpers.LessonFields(invite.PersonalStudentId, new DateOnly(2026, 11, 4), "23:00"));
        Assert.Equal("/personal/aulas?mes=2026-11&aviso=registrada", past.RedirectTarget());
        Assert.Equal(new DateTime(2026, 11, 5, 2, 0, 0, DateTimeKind.Utc),
            Assert.Single(await factory.LessonsOfAsync(invite.PersonalStudentId)).OccurredAtUtc);
    }

    [Fact]
    public async Task Register_PossibleDuplicate_AsksForConfirmation()
    {
        var linkId = await ActiveStudentAsync();
        var day = LessonTestHelpers.Yesterday;
        var fields = LessonTestHelpers.LessonFields(linkId, day, "18:00");
        await _personal.Client.PostRegisterLessonAsync(fields);

        var warning = await _personal.Client.PostRegisterLessonAsync(fields);

        Assert.Equal(HttpStatusCode.OK, warning.StatusCode);
        var html = await warning.TextAsync();
        Assert.Contains($"Já existe uma aula de Mariana em {day:dd/MM/yyyy} às 18:00. Se for outra aula, registre mesmo assim.", html);
        Assert.Contains("Registrar mesmo assim", html);
        Assert.Single(await _factory.LessonsOfAsync(linkId));

        var key = ConfirmedKey().Match(html).Groups[1].Value;
        var confirmed = await _personal.Client.PostRegisterLessonAsync(
            new Dictionary<string, string>(fields) { ["Input.ConfirmedDuplicate"] = key });

        Assert.StartsWith("/personal/aulas?mes=", confirmed.RedirectTarget());
        Assert.Equal(2, (await _factory.LessonsOfAsync(linkId)).Count);
    }

    [Fact]
    public async Task Register_ConfirmationForAnotherSlot_DoesNotSkipDuplicateCheck()
    {
        var linkId = await ActiveStudentAsync();
        var day = LessonTestHelpers.Yesterday;
        await _factory.AddLessonAsync(linkId, LessonTestHelpers.Utc(day, 9));
        var otherSlotKey = RegisterLessonFields.DuplicateKey(linkId, LessonTestHelpers.Utc(day, 8));

        var response = await _personal.Client.PostRegisterLessonAsync(
            new Dictionary<string, string>(LessonTestHelpers.LessonFields(linkId, day, "09:00"))
            {
                ["Input.ConfirmedDuplicate"] = otherSlotKey
            });

        Assert.Contains("Já existe uma aula de Mariana", await response.TextAsync());
        Assert.Single(await _factory.LessonsOfAsync(linkId));
    }

    [Fact]
    public async Task Register_SameSlotAsDeletedLesson_IsNotADuplicate()
    {
        var linkId = await ActiveStudentAsync();
        var day = LessonTestHelpers.Yesterday;
        await _factory.AddLessonAsync(linkId, LessonTestHelpers.Utc(day, 9), deleted: true);

        var response = await _personal.Client.PostRegisterLessonAsync(LessonTestHelpers.LessonFields(linkId, day, "09:00"));

        Assert.StartsWith("/personal/aulas?mes=", response.RedirectTarget());
    }

    [Fact]
    public async Task Register_AfterPriceChange_KeepsOldLessonsPrice()
    {
        var linkId = await ActiveStudentAsync();
        await _personal.Client.PostRegisterLessonAsync(LessonTestHelpers.LessonFields(linkId, time: "08:00"));
        await _factory.WithDbAsync(db => db.PersonalStudents.Where(s => s.Id == linkId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LessonPrice, 95.50m)));

        await _personal.Client.PostRegisterLessonAsync(LessonTestHelpers.LessonFields(linkId, time: "09:00"));

        var lessons = await _factory.LessonsOfAsync(linkId);
        Assert.Equal([80.00m, 95.50m], lessons.Select(l => l.UnitPrice));
    }

    [Fact]
    public async Task Register_ForOtherPersonalsStudent_IsRejected()
    {
        var owner = await _factory.LoginPersonalAsync();
        var invite = await _factory.CreateInviteAsync(owner.Account.Id);

        var response = await _personal.Client.PostRegisterLessonAsync(LessonTestHelpers.LessonFields(invite.PersonalStudentId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Escolha o aluno.", await response.TextAsync());
        Assert.Empty(await _factory.LessonsOfAsync(invite.PersonalStudentId));
    }

    [Fact]
    public async Task Register_InvalidFields_ShowsMessages()
    {
        var response = await _personal.Client.PostRegisterLessonAsync(new Dictionary<string, string>
        {
            ["Input.StudentId"] = "",
            ["Input.Date"] = "",
            ["Input.Time"] = "",
            ["Input.Duration"] = "50",
            ["Input.Notes"] = new string('a', 501)
        });

        var html = await response.TextAsync();
        Assert.Contains("Escolha o aluno.", html);
        Assert.Contains("Informe a data da aula.", html);
        Assert.Contains("Informe o horário da aula.", html);
        Assert.Contains("Escolha 30, 45, 60 ou 90 minutos.", html);
        Assert.Contains("A observação pode ter até 500 caracteres.", html);
    }

    [Fact]
    public async Task Handler_ParallelRegistrationsOfSameSlot_OnlyOneWithoutConfirmation()
    {
        var linkId = await ActiveStudentAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<RegisterLessonHandler>();
            var input = new RegisterLessonInput
            {
                StudentId = linkId.ToString(),
                Date = LessonTestHelpers.Yesterday.ToString("yyyy-MM-dd"),
                Time = "12:00"
            };
            return await handler.HandleAsync(new RegisterLessonCommand(_personal.Account.Id, input, null), CancellationToken.None);
        }));

        Assert.Single(results, r => r.Outcome == RegisterLessonOutcome.Succeeded);
        Assert.Equal(2, results.Count(r => r.Outcome == RegisterLessonOutcome.PossibleDuplicate));
        Assert.Single(await _factory.LessonsOfAsync(linkId));
    }

    [Fact]
    public async Task Page_WithoutStudents_ShowsEmptyState()
    {
        var page = await (await _personal.Client.GetAsync("/personal/aulas/nova")).TextAsync();

        Assert.Contains("Nenhum aluno para registrar aula", page);
    }

    [GeneratedRegex("name=\"Input.ConfirmedDuplicate\" value=\"([^\"]+)\"")]
    private static partial Regex ConfirmedKey();

    [GeneratedRegex("data-ph-lesson-invited hidden")]
    private static partial Regex InvitedNoticeHidden();
}
