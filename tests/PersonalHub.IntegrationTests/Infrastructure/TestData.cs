using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.IntegrationTests.Infrastructure;

/// <summary>Cria dados fictícios mínimos. Cada chamada usa ids novos, então os testes não interferem entre si.</summary>
public static class TestData
{
    public static async Task<ApplicationUser> AddUserAsync(this AppDbContext context)
    {
        var name = $"user-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = name,
            NormalizedEmail = name.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public static async Task<PersonalStudent> AddPersonalStudentAsync(this AppDbContext context, short billingDueDay = 10)
    {
        var personalUser = await context.AddUserAsync();
        var profile = new PersonalProfile
        {
            Id = Guid.NewGuid(),
            UserId = personalUser.Id,
            DisplayName = "Personal Teste",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.PersonalProfiles.Add(profile);

        var link = NewPersonalStudent(profile.Id, billingDueDay);
        context.PersonalStudents.Add(link);
        await context.SaveChangesAsync();
        return link;
    }

    public static PersonalStudent NewPersonalStudent(Guid personalId, short billingDueDay = 10) => new()
    {
        Id = Guid.NewGuid(),
        PersonalId = personalId,
        StudentName = "Aluno Teste",
        StudentEmail = $"aluno-{Guid.NewGuid():N}@example.test",
        StudentPhone = "+5511999990000",
        LessonPrice = 120.00m,
        BillingDueDay = billingDueDay,
        Status = PersonalStudentStatus.Invited,
        CreatedAtUtc = DateTime.UtcNow
    };

    public static async Task<Lesson> AddLessonAsync(this AppDbContext context, PersonalStudent link)
    {
        var personalUserId = context.PersonalProfiles.Single(p => p.Id == link.PersonalId).UserId;
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            PersonalStudentId = link.Id,
            CreatedByUserId = personalUserId,
            OccurredAtUtc = DateTime.UtcNow.AddDays(-1),
            UnitPrice = link.LessonPrice,
            Status = LessonStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
        context.Lessons.Add(lesson);
        await context.SaveChangesAsync();
        return lesson;
    }

    public static BillingPeriod NewBillingPeriod(Guid personalStudentId, DateOnly referenceMonth) => new()
    {
        Id = Guid.NewGuid(),
        PersonalStudentId = personalStudentId,
        ReferenceMonth = referenceMonth,
        PeriodStart = referenceMonth,
        PeriodEnd = referenceMonth.AddMonths(1),
        DueDate = BillingCalendar.DueDateIn(referenceMonth.AddMonths(1).Year, referenceMonth.AddMonths(1).Month, 10),
        TotalAmount = 120.00m,
        Status = BillingPeriodStatus.Issued,
        IssuedAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow
    };
}
