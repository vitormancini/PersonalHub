using Microsoft.EntityFrameworkCore;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Domain.Lessons;

namespace PersonalHub.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public class ConcurrencyTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Lesson_ConflictingUpdatesThrowConcurrencyException()
    {
        Guid lessonId;
        Guid studentId;
        await using (var setup = fixture.CreateContext())
        {
            var link = await setup.AddPersonalStudentAsync();
            lessonId = (await setup.AddLessonAsync(link)).Id;
            studentId = (await setup.AddUserAsync()).Id;
        }

        // Dois usuários carregam a mesma aula pendente.
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();
        var lessonA = await first.Lessons.SingleAsync(l => l.Id == lessonId);
        var lessonB = await second.Lessons.SingleAsync(l => l.Id == lessonId);

        lessonA.Status = LessonStatus.Approved;
        lessonA.RespondedAtUtc = DateTime.UtcNow;
        lessonA.RespondedByUserId = studentId;
        await first.SaveChangesAsync();

        lessonB.Status = LessonStatus.Rejected;
        lessonB.RespondedAtUtc = DateTime.UtcNow;
        lessonB.RespondedByUserId = studentId;
        lessonB.RejectionReason = "Não aconteceu";

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var check = fixture.CreateContext();
        Assert.Equal(LessonStatus.Approved, (await check.Lessons.SingleAsync(l => l.Id == lessonId)).Status);
    }
}
