using PersonalHub.Web.Domain.Students;

namespace PersonalHub.UnitTests.Domain.Students;

public class InviteRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    private static InviteState Evaluate(
        DateTime? expires = null,
        DateTime? used = null,
        DateTime? revoked = null,
        PersonalStudentStatus status = PersonalStudentStatus.Invited,
        bool personalActive = true) =>
        InviteRules.Evaluate(expires ?? Now.AddDays(1), used, revoked, status, personalActive, Now);

    [Fact]
    public void Valid_WhenFreeAndNotExpired() => Assert.Equal(InviteState.Valid, Evaluate());

    [Fact]
    public void Expired_AtTheExactExpiryInstant() => Assert.Equal(InviteState.Expired, Evaluate(expires: Now));

    [Fact]
    public void Used_WhenUsedAtIsSet() => Assert.Equal(InviteState.Used, Evaluate(used: Now.AddHours(-1)));

    [Fact]
    public void Used_WhenLinkAlreadyActive() => Assert.Equal(InviteState.Used, Evaluate(status: PersonalStudentStatus.Active));

    [Fact]
    public void Used_WinsOverExpired() =>
        Assert.Equal(InviteState.Used, Evaluate(expires: Now.AddDays(-1), used: Now.AddDays(-2)));

    [Fact]
    public void NotFound_WhenRevoked() => Assert.Equal(InviteState.NotFound, Evaluate(revoked: Now.AddHours(-1)));

    [Fact]
    public void NotFound_WhenLinkInactive() =>
        Assert.Equal(InviteState.NotFound, Evaluate(status: PersonalStudentStatus.Inactive, used: Now));

    [Fact]
    public void NotFound_WhenPersonalInactive() => Assert.Equal(InviteState.NotFound, Evaluate(personalActive: false));
}
