using Microsoft.EntityFrameworkCore;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Lessons;

namespace PersonalHub.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public class ConstraintTests(DatabaseFixture fixture)
{
    private const string UniqueViolation = "23505";
    private const string CheckViolation = "23514";

    [Fact]
    public async Task BillingPeriod_RejectsSecondPeriodForSameMonth()
    {
        await using var context = fixture.CreateContext();
        var link = await context.AddPersonalStudentAsync();
        var month = new DateOnly(2027, 1, 1);
        context.BillingPeriods.Add(TestData.NewBillingPeriod(link.Id, month));
        await context.SaveChangesAsync();

        context.BillingPeriods.Add(TestData.NewBillingPeriod(link.Id, month));

        await DbAssert.ViolatesAsync(UniqueViolation, "IX_BillingPeriod_PersonalStudentId_ReferenceMonth",
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task BillingPeriod_AllowsSameMonthForAnotherLink()
    {
        await using var context = fixture.CreateContext();
        var month = new DateOnly(2027, 1, 1);
        var first = await context.AddPersonalStudentAsync();
        var second = await context.AddPersonalStudentAsync();

        context.BillingPeriods.Add(TestData.NewBillingPeriod(first.Id, month));
        context.BillingPeriods.Add(TestData.NewBillingPeriod(second.Id, month));

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task BillingPeriodItem_RejectsChargingSameLessonTwice()
    {
        await using var context = fixture.CreateContext();
        var link = await context.AddPersonalStudentAsync();
        var lesson = await context.AddLessonAsync(link);
        var january = TestData.NewBillingPeriod(link.Id, new DateOnly(2027, 1, 1));
        var february = TestData.NewBillingPeriod(link.Id, new DateOnly(2027, 2, 1));
        context.BillingPeriods.AddRange(january, february);
        context.BillingPeriodItems.Add(LessonCharge(january.Id, lesson.Id));
        await context.SaveChangesAsync();

        context.BillingPeriodItems.Add(LessonCharge(february.Id, lesson.Id));

        await DbAssert.ViolatesAsync(UniqueViolation, "IX_BillingPeriodItem_LessonId_Kind",
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task BillingPeriodItem_AllowsOneChargeAndOneCreditForSameLesson()
    {
        await using var context = fixture.CreateContext();
        var link = await context.AddPersonalStudentAsync();
        var lesson = await context.AddLessonAsync(link);
        var january = TestData.NewBillingPeriod(link.Id, new DateOnly(2027, 1, 1));
        var february = TestData.NewBillingPeriod(link.Id, new DateOnly(2027, 2, 1));
        context.BillingPeriods.AddRange(january, february);
        context.BillingPeriodItems.Add(LessonCharge(january.Id, lesson.Id));
        context.BillingPeriodItems.Add(new BillingPeriodItem
        {
            Id = Guid.NewGuid(),
            BillingPeriodId = february.Id,
            Kind = BillingPeriodItemKind.Adjustment,
            LessonId = lesson.Id,
            Description = "Crédito de aula excluída",
            Amount = -lesson.UnitPrice,
            CreatedAtUtc = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public async Task PersonalStudent_RejectsBillingDueDayOutOfRange(short dueDay)
    {
        await using var context = fixture.CreateContext();
        var existing = await context.AddPersonalStudentAsync();

        context.PersonalStudents.Add(TestData.NewPersonalStudent(existing.PersonalId, dueDay));

        await DbAssert.ViolatesAsync(CheckViolation, "CK_PersonalStudent_BillingDueDay",
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Lesson_RejectsRejectionWithoutReason()
    {
        await using var context = fixture.CreateContext();
        var link = await context.AddPersonalStudentAsync();
        var lesson = await context.AddLessonAsync(link);
        var student = await context.AddUserAsync();

        lesson.Status = LessonStatus.Rejected;
        lesson.RespondedAtUtc = DateTime.UtcNow;
        lesson.RespondedByUserId = student.Id;

        await DbAssert.ViolatesAsync(CheckViolation, "CK_Lesson_RejectionReason",
            () => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Payment_RejectsSecondActivePaymentForSamePeriod()
    {
        await using var context = fixture.CreateContext();
        var link = await context.AddPersonalStudentAsync();
        var student = await context.AddUserAsync();
        var period = TestData.NewBillingPeriod(link.Id, new DateOnly(2027, 1, 1));
        context.BillingPeriods.Add(period);
        context.Payments.Add(ReportedPayment(period, student.Id));
        await context.SaveChangesAsync();

        context.Payments.Add(ReportedPayment(period, student.Id));

        await DbAssert.ViolatesAsync(UniqueViolation, "IX_Payment_BillingPeriodId_Active",
            () => context.SaveChangesAsync());
    }

    private static BillingPeriodItem LessonCharge(Guid billingPeriodId, Guid lessonId) => new()
    {
        Id = Guid.NewGuid(),
        BillingPeriodId = billingPeriodId,
        Kind = BillingPeriodItemKind.Lesson,
        LessonId = lessonId,
        Description = "Aula",
        Amount = 120.00m,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static Payment ReportedPayment(BillingPeriod period, Guid studentUserId) => new()
    {
        Id = Guid.NewGuid(),
        BillingPeriodId = period.Id,
        Amount = period.TotalAmount,
        Status = PaymentStatus.Reported,
        ReportedByUserId = studentUserId,
        ReportedAtUtc = DateTime.UtcNow,
        CreatedAtUtc = DateTime.UtcNow
    };
}
