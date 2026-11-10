using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PersonalHub.IntegrationTests.Lessons;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Features.Billing.CloseBilling;
using PersonalHub.Web.Features.Lessons.DeleteLesson;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.IntegrationTests.Billing;

/// <summary>Atalhos dos testes de demonstrativo (Fase 4). O job é chamado direto, com relógio parado.</summary>
public static class BillingTestHelpers
{
    /// <summary>Horário de Brasília como instante, para o relógio dos testes.</summary>
    public static DateTimeOffset SpClock(int year, int month, int day, int hour = 6, int minute = 0) =>
        new(SaoPaulo.ToUtc(new DateOnly(year, month, day), new TimeOnly(hour, minute))!.Value);

    public static DateTime Sp(int year, int month, int day, int hour = 10, int minute = 0) =>
        SaoPaulo.ToUtc(new DateOnly(year, month, day), new TimeOnly(hour, minute))!.Value;

    public static async Task<T> WithJobAsync<T>(
        this WebApplicationFactory<Program> factory, DateTimeOffset now, Func<BillingClosingJob, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var job = new BillingClosingJob(scope.ServiceProvider.GetRequiredService<AppDbContext>(), new FixedTimeProvider(now),
            NullLogger<BillingClosingJob>.Instance);
        return await action(job);
    }

    public static Task<ClosingRunResult> RunClosingAsync(this WebApplicationFactory<Program> factory, DateTimeOffset now) =>
        factory.WithJobAsync(now, job => job.RunAsync(CancellationToken.None));

    public static Task<CloseLinkOutcome> CloseAsync(
        this WebApplicationFactory<Program> factory, Guid personalStudentId, DateOnly referenceMonth, DateTimeOffset now) =>
        factory.WithJobAsync(now, job => job.CloseLinkAsync(personalStudentId, referenceMonth, CancellationToken.None));

    public static Task<List<BillingPeriod>> PeriodsOfAsync(this WebApplicationFactory<Program> factory, Guid personalStudentId) =>
        factory.WithDbAsync(db => db.BillingPeriods.AsNoTracking().Where(p => p.PersonalStudentId == personalStudentId)
            .OrderBy(p => p.ReferenceMonth).ToListAsync());

    public static Task<List<BillingPeriodItem>> ItemsOfAsync(this WebApplicationFactory<Program> factory, Guid billingPeriodId) =>
        factory.WithDbAsync(db => db.BillingPeriodItems.AsNoTracking().Where(i => i.BillingPeriodId == billingPeriodId)
            .OrderBy(i => i.Kind).ThenBy(i => i.Description).ToListAsync());

    /// <summary>Aula confirmada num instante conhecido (o corte do fechamento olha a hora da confirmação).</summary>
    public static async Task<Lesson> AddApprovedLessonAsync(
        this WebApplicationFactory<Program> factory, Guid personalStudentId, DateTime occurredAtUtc, DateTime? respondedAtUtc = null)
    {
        var lesson = await factory.AddLessonAsync(personalStudentId, occurredAtUtc, LessonStatus.Approved);
        await factory.SetRespondedAtAsync(lesson.Id, respondedAtUtc ?? occurredAtUtc.AddHours(3));
        return lesson;
    }

    public static Task<int> SetRespondedAtAsync(this WebApplicationFactory<Program> factory, Guid lessonId, DateTime respondedAtUtc) =>
        factory.WithDbAsync(db => db.Lessons.Where(l => l.Id == lessonId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.RespondedAtUtc, respondedAtUtc)));

    /// <summary>Confirma uma aula pendente num instante conhecido.</summary>
    public static Task<int> ApproveAtAsync(this WebApplicationFactory<Program> factory, Guid lessonId, DateTime respondedAtUtc) =>
        factory.WithDbAsync(async db =>
        {
            var lesson = await db.Lessons.SingleAsync(l => l.Id == lessonId);
            var studentId = await db.PersonalStudents.Where(s => s.Id == lesson.PersonalStudentId).Select(s => s.StudentUserId!.Value).SingleAsync();
            lesson.Approve(studentId, respondedAtUtc);
            return await db.SaveChangesAsync();
        });

    public static Task<int> SetBillingAsync(
        this WebApplicationFactory<Program> factory, Guid personalStudentId, short? dueDay = null, decimal? price = null) =>
        factory.WithDbAsync(db => db.PersonalStudents.Where(s => s.Id == personalStudentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.BillingDueDay, x => dueDay ?? x.BillingDueDay)
                .SetProperty(x => x.LessonPrice, x => price ?? x.LessonPrice)));

    /// <summary>Exclusão pelo handler real (com motivo), num instante conhecido.</summary>
    public static async Task<DeleteLessonOutcome> DeleteLessonAtAsync(
        this WebApplicationFactory<Program> factory, Guid personalUserId, Guid lessonId, DateTimeOffset now)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = new DeleteLessonHandler(scope.ServiceProvider.GetRequiredService<AppDbContext>(), new FixedTimeProvider(now),
            NullLogger<DeleteLessonHandler>.Instance);
        return await handler.HandleAsync(personalUserId, lessonId, "Registrei errado", "teste", CancellationToken.None);
    }
}
