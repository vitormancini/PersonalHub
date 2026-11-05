using PersonalHub.Web.Domain.Students;

namespace PersonalHub.UnitTests.Domain.Students;

public class PersonalStudentTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private static PersonalStudent Link(PersonalStudentStatus status, Guid? studentUserId = null) => new()
    {
        Id = Guid.NewGuid(),
        PersonalId = Guid.NewGuid(),
        StudentUserId = studentUserId,
        StudentName = "Mariana Lopes",
        StudentEmail = "mariana@example.test",
        StudentPhone = "+5511987654321",
        LessonPrice = 80m,
        BillingDueDay = 10,
        Status = status,
        EndedAtUtc = status == PersonalStudentStatus.Inactive ? Now.AddDays(-3) : null
    };

    [Theory]
    [InlineData(PersonalStudentStatus.Invited, true)]
    [InlineData(PersonalStudentStatus.Active, false)]
    [InlineData(PersonalStudentStatus.Inactive, false)]
    public void CanEditEmail_OnlyWhileInvited(PersonalStudentStatus status, bool expected) =>
        Assert.Equal(expected, Link(status).CanEditEmail);

    [Theory]
    [InlineData(PersonalStudentStatus.Invited)]
    [InlineData(PersonalStudentStatus.Active)]
    public void Inactivate_SetsStatusAndEndedAt(PersonalStudentStatus status)
    {
        var link = Link(status, status == PersonalStudentStatus.Active ? Guid.NewGuid() : null);

        link.Inactivate(Now);

        Assert.Equal(PersonalStudentStatus.Inactive, link.Status);
        Assert.Equal(Now, link.EndedAtUtc);
    }

    [Fact]
    public void Inactivate_WhenAlreadyInactive_Throws()
    {
        var link = Link(PersonalStudentStatus.Inactive);
        var endedAt = link.EndedAtUtc;

        Assert.Throws<InvalidOperationException>(() => link.Inactivate(Now));
        Assert.Equal(endedAt, link.EndedAtUtc);
    }

    [Fact]
    public void Reactivate_WithAccount_GoesBackToActive()
    {
        var link = Link(PersonalStudentStatus.Inactive, Guid.NewGuid());

        link.Reactivate();

        Assert.Equal(PersonalStudentStatus.Active, link.Status);
        Assert.Null(link.EndedAtUtc);
    }

    [Fact]
    public void Reactivate_WithoutAccount_GoesBackToInvited()
    {
        var link = Link(PersonalStudentStatus.Inactive);

        link.Reactivate();

        Assert.Equal(PersonalStudentStatus.Invited, link.Status);
        Assert.Null(link.EndedAtUtc);
    }

    [Theory]
    [InlineData(PersonalStudentStatus.Invited)]
    [InlineData(PersonalStudentStatus.Active)]
    public void Reactivate_WhenNotInactive_Throws(PersonalStudentStatus status)
    {
        var link = Link(status);

        Assert.Throws<InvalidOperationException>(link.Reactivate);
        Assert.Equal(status, link.Status);
    }
}
