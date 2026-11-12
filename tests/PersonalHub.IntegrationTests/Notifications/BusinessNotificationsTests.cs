using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Billing;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Lessons;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Features.Billing.CloseBilling;
using PersonalHub.Web.Features.Billing.Payments;
using PersonalHub.Web.Features.Lessons;
using PersonalHub.Web.Features.Lessons.DeleteLesson;
using PersonalHub.Web.Features.Lessons.RespondLesson;
using PersonalHub.Web.Features.Notifications;
using static PersonalHub.IntegrationTests.Billing.BillingTestHelpers;
using T = PersonalHub.Web.Domain.Notifications.NotificationTemplates;

namespace PersonalHub.IntegrationTests.Notifications;

/// <summary>
/// Cada evento de negócio enfileira o template certo, uma vez, para a pessoa certa, com botão e sem texto livre
/// (D-060 a D-062). Relógio parado em 12/11/2026 12:00 (Brasília).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BusinessNotificationsTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string PersonalPhone = "+5511900000001";
    private const string FreeText = "Texto livre que não pode sair do app";
    private static readonly DateTimeOffset Now = SpClock(2026, 11, 12, 12);

    private WebApplicationFactory<Program> _factory = default!;
    private LoggedPersonal _personal = default!;
    private TestAccount _student = default!;
    private string _studentPhone = default!;
    private Guid _linkId;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString).WithClock(Now);
        _personal = await _factory.LoginPersonalAsync();
        await _factory.SetPersonalNameAsync(_personal.Account.Id, "Carlos Lima");
        await _factory.WithDbAsync(db => db.PersonalProfiles.Where(p => p.UserId == _personal.Account.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PhoneNumber, PersonalPhone)));
        _linkId = (await _factory.CreateInviteAsync(_personal.Account.Id, name: "Mariana Lopes")).PersonalStudentId;
        _student = await _factory.LinkStudentAsync(_linkId);
        _studentPhone = await _factory.WithDbAsync(db => db.Users.Where(u => u.Id == _student.Id).Select(u => u.PhoneNumber!).SingleAsync());
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<TResult> InScopeAsync<THandler, TResult>(Func<THandler, Task<TResult>> action) where THandler : notnull
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<THandler>());
    }

    private Task<NotificationOutbox> SingleAsync(string key) =>
        _factory.WithDbAsync(db => db.NotificationOutbox.AsNoTracking().SingleAsync(o => o.IdempotencyKey == key));

    private static NotificationPayload Payload(NotificationOutbox row) => NotificationPayload.FromJson(row.Payload)!;

    private Task<int> CountAsync(string template) =>
        _factory.WithDbAsync(db => db.NotificationOutbox.CountAsync(o =>
            o.EventType == template && (o.RecipientUserId == _student.Id || o.RecipientUserId == _personal.Account.Id)));

    private void AssertToStudent(NotificationOutbox row)
    {
        Assert.Equal(_student.Id, row.RecipientUserId);
        Assert.Equal(_studentPhone, row.RecipientPhone);
        Assert.False(row.IsAuthentication);
        Assert.Equal(NotificationStatus.Pending, row.Status);
    }

    private void AssertToPersonal(NotificationOutbox row)
    {
        Assert.Equal(_personal.Account.Id, row.RecipientUserId);
        Assert.Equal(PersonalPhone, row.RecipientPhone);
    }

    [Fact]
    public async Task LessonRegistered_NotifiesTheStudentOnce()
    {
        var response = await _personal.Client.PostRegisterLessonAsync(LessonTestHelpers.LessonFields(_linkId, time: "07:30",
            notes: FreeText));
        Assert.Contains("aviso=registrada", response.RedirectTarget());
        var lesson = Assert.Single(await _factory.LessonsOfAsync(_linkId));

        var row = await SingleAsync($"lesson-created:{lesson.Id}");

        Assert.Equal(T.LessonCreated, row.EventType);
        AssertToStudent(row);
        Assert.Equal(Now.UtcDateTime, row.NextAttemptAtUtc);
        Assert.Equal(["Mariana", "Carlos Lima", LessonDisplay.DayMonth(lesson.OccurredAtUtc), "07:30"], Payload(row).Parameters);
        Assert.Equal($"aluno/aulas/{lesson.Id}", Payload(row).ButtonUrlSuffix);
        Assert.DoesNotContain(FreeText, row.Payload);
        Assert.Equal(1, await CountAsync(T.LessonCreated));
    }

    [Fact]
    public async Task LessonRegistered_ForInvitedStudent_HasNoPhone()
    {
        var invited = await _factory.CreateInviteAsync(_personal.Account.Id);
        await _personal.Client.PostRegisterLessonAsync(LessonTestHelpers.LessonFields(invited.PersonalStudentId));
        var lesson = Assert.Single(await _factory.LessonsOfAsync(invited.PersonalStudentId));

        var row = await SingleAsync($"lesson-created:{lesson.Id}");

        Assert.Null(row.RecipientUserId);
        Assert.Null(row.RecipientPhone);
    }

    [Fact]
    public async Task StudentWithoutConfirmedPhone_GetsNoPhone()
    {
        await _factory.WithDbAsync(db => db.Users.Where(u => u.Id == _student.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PhoneNumberConfirmed, false)));
        var lesson = await _factory.AddLessonAsync(_linkId, Sp(2026, 11, 11, 7));
        await InScopeAsync<DeleteLessonHandler, DeleteLessonOutcome>(h => h.HandleAsync(_personal.Account.Id, lesson.Id, null, null, default));
        var approved = await _factory.AddLessonAsync(_linkId, Sp(2026, 11, 11, 8), LessonStatus.Approved);

        await InScopeAsync<DeleteLessonHandler, DeleteLessonOutcome>(h => h.HandleAsync(_personal.Account.Id, approved.Id, "Duplicada", null, default));

        var row = await SingleAsync($"lesson-deleted:{approved.Id}");
        Assert.Equal(_student.Id, row.RecipientUserId);
        Assert.Null(row.RecipientPhone);
    }

    [Fact]
    public async Task LessonApproved_NotifiesThePersonal_OnceEvenWithADoubleTap()
    {
        var lesson = await _factory.AddLessonAsync(_linkId, Sp(2026, 11, 11, 7, 30));

        Assert.Equal(RespondLessonOutcome.Succeeded, await InScopeAsync<RespondLessonHandler, RespondLessonOutcome>(
            h => h.ApproveAsync(_student.Id, lesson.Id, null, default)));
        Assert.Equal(RespondLessonOutcome.AlreadyResponded, await InScopeAsync<RespondLessonHandler, RespondLessonOutcome>(
            h => h.ApproveAsync(_student.Id, lesson.Id, null, default)));

        var row = await SingleAsync($"lesson-approved:{lesson.Id}");
        Assert.Equal(T.LessonApproved, row.EventType);
        AssertToPersonal(row);
        Assert.Equal(["Mariana Lopes", "11/11", "07:30"], Payload(row).Parameters);
        Assert.Equal("personal/aulas", Payload(row).ButtonUrlSuffix);
        Assert.Equal(1, await CountAsync(T.LessonApproved));
    }

    [Fact]
    public async Task LessonRejected_NotifiesThePersonal_WithoutTheReason()
    {
        var lesson = await _factory.AddLessonAsync(_linkId, Sp(2026, 11, 11, 18));

        await InScopeAsync<RespondLessonHandler, RespondLessonOutcome>(h => h.RejectAsync(_student.Id, lesson.Id, FreeText, null, default));

        var row = await SingleAsync($"lesson-rejected:{lesson.Id}");
        Assert.Equal(T.LessonRejected, row.EventType);
        AssertToPersonal(row);
        Assert.Equal(["Mariana Lopes", "11/11", "18:00"], Payload(row).Parameters);
        Assert.Equal($"personal/aulas/{lesson.Id}", Payload(row).ButtonUrlSuffix);
        Assert.DoesNotContain(FreeText, row.Payload);
    }

    [Fact]
    public async Task RejectWithInvalidReason_ChangesNothing_AndQueuesNothing()
    {
        var lesson = await _factory.AddLessonAsync(_linkId, Sp(2026, 11, 11, 18));

        Assert.Equal(RespondLessonOutcome.ReasonRequired, await InScopeAsync<RespondLessonHandler, RespondLessonOutcome>(
            h => h.RejectAsync(_student.Id, lesson.Id, "  ", null, default)));

        Assert.Null(await _factory.OutboxByKeyAsync($"lesson-rejected:{lesson.Id}"));
    }

    [Fact]
    public async Task DeletingAPendingLesson_DoesNotNotify_ButAnAnsweredOneDoes()
    {
        var pending = await _factory.AddLessonAsync(_linkId, Sp(2026, 11, 10, 7));
        var answered = await _factory.AddLessonAsync(_linkId, Sp(2026, 11, 10, 9), LessonStatus.Rejected);
        await _factory.WithDbAsync(db => db.Lessons.Where(l => l.Id == answered.Id).ExecuteUpdateAsync(s => s
            .SetProperty(l => l.RejectionReason, "Não fui").SetProperty(l => l.RespondedAtUtc, DateTime.UtcNow)
            .SetProperty(l => l.RespondedByUserId, _student.Id)));

        await InScopeAsync<DeleteLessonHandler, DeleteLessonOutcome>(h => h.HandleAsync(_personal.Account.Id, pending.Id, null, null, default));
        await InScopeAsync<DeleteLessonHandler, DeleteLessonOutcome>(h => h.HandleAsync(_personal.Account.Id, answered.Id, FreeText, null, default));

        Assert.Null(await _factory.OutboxByKeyAsync($"lesson-deleted:{pending.Id}"));
        var row = await SingleAsync($"lesson-deleted:{answered.Id}");
        Assert.Equal(T.LessonDeleted, row.EventType);
        AssertToStudent(row);
        Assert.Equal(["Mariana", "Carlos Lima", "10/11", "09:00"], Payload(row).Parameters);
        Assert.Equal("aluno/aulas", Payload(row).ButtonUrlSuffix);
        Assert.DoesNotContain(FreeText, row.Payload);
    }

    [Fact]
    public async Task StatementIssued_NotifiesTheStudent_OncePerStatement()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 10, 5, 7));
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 10, 19, 18, 30));

        Assert.Equal(CloseLinkOutcome.Issued, await _factory.CloseAsync(_linkId, new DateOnly(2026, 10, 1), SpClock(2026, 11, 10)));
        Assert.Equal(CloseLinkOutcome.AlreadyClosed, await _factory.CloseAsync(_linkId, new DateOnly(2026, 10, 1), SpClock(2026, 11, 10)));
        await _factory.RunClosingAsync(SpClock(2026, 11, 10, 7));
        var period = Assert.Single(await _factory.PeriodsOfAsync(_linkId));

        var row = await SingleAsync($"statement-issued:{period.Id}");
        Assert.Equal(T.StatementIssued, row.EventType);
        AssertToStudent(row);
        Assert.Equal(["Mariana", "outubro", "Carlos Lima", "2", "R$ 160,00", "10/11"], Payload(row).Parameters);
        Assert.Equal($"aluno/pagamentos/{period.Id}", Payload(row).ButtonUrlSuffix);
        Assert.Equal(1, await CountAsync(T.StatementIssued));
    }

    [Fact]
    public async Task StatementIssuedAt6am_IsScheduledFor8am()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 10, 5, 7));

        await _factory.CloseAsync(_linkId, new DateOnly(2026, 10, 1), SpClock(2026, 11, 10, 6));

        var period = Assert.Single(await _factory.PeriodsOfAsync(_linkId));
        Assert.Equal(Sp(2026, 11, 10, 8), (await SingleAsync($"statement-issued:{period.Id}")).NextAttemptAtUtc);
    }

    [Fact]
    public async Task ZeroTotalStatement_DoesNotNotify()
    {
        var lesson = await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 9, 5, 7));
        await _factory.CloseAsync(_linkId, new DateOnly(2026, 9, 1), SpClock(2026, 10, 10));
        await _factory.DeleteLessonAtAsync(_personal.Account.Id, lesson.Id, SpClock(2026, 10, 20));

        await _factory.CloseAsync(_linkId, new DateOnly(2026, 10, 1), SpClock(2026, 11, 10));

        var october = (await _factory.PeriodsOfAsync(_linkId)).Single(p => p.ReferenceMonth == new DateOnly(2026, 10, 1));
        Assert.Null(await _factory.OutboxByKeyAsync($"statement-issued:{october.Id}"));
    }

    [Fact]
    public async Task Payments_NotifyBothSides_WithoutNotes()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 10, 5, 7));
        await _factory.CloseAsync(_linkId, new DateOnly(2026, 10, 1), SpClock(2026, 11, 10));
        var periodId = Assert.Single(await _factory.PeriodsOfAsync(_linkId)).Id;

        await InScopeAsync<PaymentHandler, PaymentOutcome>(h => h.ReportAsync(_student.Id, periodId, FreeText, null, default));
        var first = await _factory.WithDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.BillingPeriodId == periodId));
        var reported = await SingleAsync($"payment-reported:{first.Id}");
        Assert.Equal(T.PaymentReported, reported.EventType);
        AssertToPersonal(reported);
        Assert.Equal(["Mariana Lopes", "outubro", "R$ 80,00"], Payload(reported).Parameters);
        Assert.Equal($"personal/financeiro/{periodId}", Payload(reported).ButtonUrlSuffix);
        Assert.DoesNotContain(FreeText, reported.Payload);

        await InScopeAsync<PaymentHandler, PaymentOutcome>(h => h.RejectAsync(_personal.Account.Id, periodId, FreeText, null, default));
        var rejected = await SingleAsync($"payment-rejected:{first.Id}");
        Assert.Equal(T.PaymentRejected, rejected.EventType);
        AssertToStudent(rejected);
        Assert.Equal(["Mariana", "Carlos Lima", "outubro"], Payload(rejected).Parameters);
        Assert.Equal($"aluno/pagamentos/{periodId}", Payload(rejected).ButtonUrlSuffix);
        Assert.DoesNotContain(FreeText, rejected.Payload);

        await InScopeAsync<PaymentHandler, PaymentOutcome>(h => h.ReportAsync(_student.Id, periodId, null, null, default));
        await InScopeAsync<PaymentHandler, PaymentOutcome>(h => h.ConfirmAsync(_personal.Account.Id, periodId, null, default));
        // Repetir a confirmação não gera outro aviso.
        Assert.Equal(PaymentOutcome.AlreadyPaid,
            await InScopeAsync<PaymentHandler, PaymentOutcome>(h => h.ConfirmAsync(_personal.Account.Id, periodId, null, default)));
        var second = await _factory.WithDbAsync(db => db.Payments.AsNoTracking()
            .SingleAsync(p => p.BillingPeriodId == periodId && p.Status == Web.Domain.Billing.PaymentStatus.Confirmed));
        var confirmed = await SingleAsync($"payment-confirmed:{second.Id}");
        Assert.Equal(T.PaymentConfirmed, confirmed.EventType);
        AssertToStudent(confirmed);
        Assert.Equal(["Mariana", "Carlos Lima", "outubro", "R$ 80,00"], Payload(confirmed).Parameters);

        Assert.Equal(2, await CountAsync(T.PaymentReported));
        Assert.Equal(1, await CountAsync(T.PaymentRejected));
        Assert.Equal(1, await CountAsync(T.PaymentConfirmed));
    }

    [Fact]
    public async Task RecordedByThePersonal_NotifiesTheStudentAsConfirmed()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2026, 10, 5, 7));
        await _factory.CloseAsync(_linkId, new DateOnly(2026, 10, 1), SpClock(2026, 11, 10));
        var periodId = Assert.Single(await _factory.PeriodsOfAsync(_linkId)).Id;

        await InScopeAsync<PaymentHandler, PaymentOutcome>(h => h.RecordAsync(_personal.Account.Id, periodId, null, default));

        var payment = await _factory.WithDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.BillingPeriodId == periodId));
        AssertToStudent(await SingleAsync($"payment-confirmed:{payment.Id}"));
    }

    [Fact]
    public async Task TemplateVariables_HaveNoLineBreaks()
    {
        await _factory.WithDbAsync(db => db.PersonalStudents.Where(s => s.Id == _linkId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StudentName, "Mariana\nLopes\t Silva")));
        var lesson = await _factory.AddLessonAsync(_linkId, Sp(2026, 11, 11, 7));

        await InScopeAsync<RespondLessonHandler, RespondLessonOutcome>(h => h.ApproveAsync(_student.Id, lesson.Id, null, default));

        Assert.Equal("Mariana Lopes Silva", Payload(await SingleAsync($"lesson-approved:{lesson.Id}")).Parameters[0]);
    }
}
