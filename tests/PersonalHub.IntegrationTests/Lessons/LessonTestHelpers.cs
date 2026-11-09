using System.Globalization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Lessons;

public sealed record LoggedStudent(TestAccount Account, HttpClient Client);

/// <summary>Relógio parado, para testar a regra de "aula no futuro" num instante conhecido.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Atalhos dos testes de aulas (Fase 3).</summary>
public static class LessonTestHelpers
{
    /// <summary>Ontem em Brasília: sempre no passado, qualquer que seja a hora em que o teste roda.</summary>
    public static DateOnly Yesterday => SaoPaulo.ToLocalDate(DateTime.UtcNow).AddDays(-1);

    public static string MonthOf(DateOnly day) => day.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static WebApplicationFactory<Program> WithClock(this WebApplicationFactory<Program> factory, DateTimeOffset now) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(now))));

    public static Dictionary<string, string> LessonFields(
        Guid personalStudentId, DateOnly? date = null, string time = "10:00", string duration = "60", string notes = "")
        => new()
        {
            ["Input.StudentId"] = personalStudentId.ToString(),
            ["Input.Date"] = (date ?? Yesterday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Input.Time"] = time,
            ["Input.Duration"] = duration,
            ["Input.Notes"] = notes
        };

    public static Task<HttpResponseMessage> PostRegisterLessonAsync(this HttpClient client, Dictionary<string, string> fields) =>
        client.PostFormAsync("/personal/aulas/nova", "nova-aula", fields);

    /// <summary>Conta de aluno ligada ao vínculo (como no aceite do convite). Reaproveita a conta se for passada.</summary>
    public static async Task<TestAccount> LinkStudentAsync(
        this WebApplicationFactory<Program> factory, Guid personalStudentId, TestAccount? account = null)
    {
        account ??= await factory.CreateAccountAsync(AppRoles.Student);
        await factory.WithDbAsync(db => db.PersonalStudents.Where(s => s.Id == personalStudentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.StudentUserId, account.Id)
                .SetProperty(x => x.Status, PersonalStudentStatus.Active)
                .SetProperty(x => x.ActivatedAtUtc, DateTime.UtcNow)));
        return account;
    }

    public static async Task<LoggedStudent> LoginStudentAsync(this WebApplicationFactory<Program> factory, TestAccount account)
    {
        var client = factory.CreateBrowser();
        await client.PostLoginAsync(account.Email, account.Password);
        return new LoggedStudent(account, client);
    }

    /// <summary>Aula gravada direto no banco, para montar cenários (o registro pela tela tem testes próprios).</summary>
    public static Task<Lesson> AddLessonAsync(
        this WebApplicationFactory<Program> factory, Guid personalStudentId, DateTime? occurredAtUtc = null,
        LessonStatus status = LessonStatus.Pending, bool deleted = false, short? duration = 60) =>
        factory.WithDbAsync(async db =>
        {
            var link = await db.PersonalStudents.SingleAsync(s => s.Id == personalStudentId);
            var personalUserId = await db.PersonalProfiles.Where(p => p.Id == link.PersonalId).Select(p => p.UserId).SingleAsync();
            var now = DateTime.UtcNow;
            var lesson = new Lesson
            {
                Id = Guid.NewGuid(),
                PersonalStudentId = link.Id,
                CreatedByUserId = personalUserId,
                OccurredAtUtc = occurredAtUtc ?? now.AddDays(-1),
                DurationMinutes = duration,
                UnitPrice = link.LessonPrice,
                Status = LessonStatus.Pending,
                CreatedAtUtc = now
            };
            if (status == LessonStatus.Approved)
            {
                lesson.Approve(link.StudentUserId!.Value, now);
            }
            else if (status == LessonStatus.Rejected)
            {
                lesson.Reject(link.StudentUserId!.Value, "Não fiz esta aula", now);
            }

            if (deleted)
            {
                lesson.Delete(personalUserId, status == LessonStatus.Pending ? null : "Registrei errado", now);
            }

            db.Lessons.Add(lesson);
            await db.SaveChangesAsync();
            return lesson;
        });

    public static Task<Lesson> LessonAsync(this WebApplicationFactory<Program> factory, Guid lessonId) =>
        factory.WithDbAsync(db => db.Lessons.AsNoTracking().SingleAsync(l => l.Id == lessonId));

    public static Task<List<Lesson>> LessonsOfAsync(this WebApplicationFactory<Program> factory, Guid personalStudentId) =>
        factory.WithDbAsync(db => db.Lessons.AsNoTracking().Where(l => l.PersonalStudentId == personalStudentId)
            .OrderBy(l => l.CreatedAtUtc).ToListAsync());

    public static Task<List<string>> LessonAuditAsync(this WebApplicationFactory<Program> factory, Guid lessonId) =>
        factory.WithDbAsync(db => db.AuditLogs.Where(a => a.EntityId == lessonId)
            .OrderBy(a => a.OccurredAtUtc).Select(a => a.Action).ToListAsync());

    public static Task<int> SetPersonalNameAsync(this WebApplicationFactory<Program> factory, Guid personalUserId, string name) =>
        factory.WithDbAsync(db => db.PersonalProfiles.Where(p => p.UserId == personalUserId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, name)));

    /// <summary>Instante UTC de um horário de Brasília.</summary>
    public static DateTime Utc(DateOnly day, int hour, int minute = 0) => SaoPaulo.ToUtc(day, new TimeOnly(hour, minute))!.Value;
}
