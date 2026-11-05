using PersonalHub.Web.Domain.Lessons;

namespace PersonalHub.UnitTests.Domain.Lessons;

public class LessonTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Student = Guid.NewGuid();
    private static readonly Guid Personal = Guid.NewGuid();

    private static Lesson Pending() => new()
    {
        Id = Guid.NewGuid(),
        PersonalStudentId = Guid.NewGuid(),
        CreatedByUserId = Personal,
        OccurredAtUtc = Now.AddHours(-2),
        UnitPrice = 80m,
        Status = LessonStatus.Pending,
        CreatedAtUtc = Now.AddHours(-1)
    };

    [Fact]
    public void Approve_Pending_RecordsResponse()
    {
        var lesson = Pending();

        lesson.Approve(Student, Now);

        Assert.Equal(LessonStatus.Approved, lesson.Status);
        Assert.Equal(Now, lesson.RespondedAtUtc);
        Assert.Equal(Student, lesson.RespondedByUserId);
        Assert.False(lesson.CanRespond);
    }

    [Fact]
    public void Reject_Pending_RecordsReason()
    {
        var lesson = Pending();

        lesson.Reject(Student, "Não fiz esta aula", Now);

        Assert.Equal(LessonStatus.Rejected, lesson.Status);
        Assert.Equal("Não fiz esta aula", lesson.RejectionReason);
        Assert.Equal(Now, lesson.RespondedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithoutReason_Throws(string? reason) =>
        Assert.Throws<ArgumentException>(() => Pending().Reject(Student, reason!, Now));

    [Fact]
    public void Respond_IsFinal()
    {
        var approved = Pending();
        approved.Approve(Student, Now);
        var rejected = Pending();
        rejected.Reject(Student, "Valor errado", Now);

        Assert.Throws<InvalidOperationException>(() => approved.Reject(Student, "Mudei de ideia", Now));
        Assert.Throws<InvalidOperationException>(() => approved.Approve(Student, Now));
        Assert.Throws<InvalidOperationException>(() => rejected.Approve(Student, Now));
    }

    [Fact]
    public void Respond_DeletedLesson_Throws()
    {
        var lesson = Pending();
        lesson.Delete(Personal, null, Now);

        Assert.False(lesson.CanRespond);
        Assert.Throws<InvalidOperationException>(() => lesson.Approve(Student, Now));
    }

    [Fact]
    public void Delete_Pending_ReasonIsOptional()
    {
        var lesson = Pending();

        lesson.Delete(Personal, "  ", Now);

        Assert.True(lesson.IsDeleted);
        Assert.Equal(Personal, lesson.DeletedByUserId);
        Assert.Null(lesson.DeletionReason);
        Assert.Equal(LessonStatus.Pending, lesson.Status);
    }

    [Theory]
    [InlineData(LessonStatus.Approved)]
    [InlineData(LessonStatus.Rejected)]
    public void Delete_Responded_RequiresReason(LessonStatus status)
    {
        var lesson = Pending();
        if (status == LessonStatus.Approved)
        {
            lesson.Approve(Student, Now);
        }
        else
        {
            lesson.Reject(Student, "Data errada", Now);
        }

        Assert.True(lesson.DeletionRequiresReason);
        Assert.Throws<ArgumentException>(() => lesson.Delete(Personal, null, Now));

        lesson.Delete(Personal, "Registrei no dia errado", Now);
        Assert.Equal("Registrei no dia errado", lesson.DeletionReason);
        Assert.Equal(status, lesson.Status);
    }

    [Fact]
    public void Delete_Twice_Throws()
    {
        var lesson = Pending();
        lesson.Delete(Personal, null, Now);

        Assert.Throws<InvalidOperationException>(() => lesson.Delete(Personal, null, Now));
    }
}
