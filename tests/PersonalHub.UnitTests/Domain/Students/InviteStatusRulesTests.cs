using PersonalHub.Web.Domain.Students;

namespace PersonalHub.UnitTests.Domain.Students;

public class InviteStatusRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Pending_WhenOpenAndNotExpired() =>
        Assert.Equal(InviteStatus.Pending, InviteStatusRules.Of(Now.AddDays(1), null, null, Now));

    [Fact]
    public void Expired_AtTheExactExpiryInstant() =>
        Assert.Equal(InviteStatus.Expired, InviteStatusRules.Of(Now, null, null, Now));

    [Fact]
    public void Cancelled_WhenRevoked_EvenIfStillWithinValidity() =>
        Assert.Equal(InviteStatus.Cancelled, InviteStatusRules.Of(Now.AddDays(1), null, Now.AddHours(-1), Now));

    [Fact]
    public void Cancelled_WhenRevokedAfterExpiring() =>
        Assert.Equal(InviteStatus.Cancelled, InviteStatusRules.Of(Now.AddDays(-1), null, Now, Now));

    [Fact]
    public void Cancelled_WhenThereIsNoInvite() =>
        Assert.Equal(InviteStatus.Cancelled, InviteStatusRules.Of(null, null, null, Now));

    [Fact]
    public void Used_WhenUsedAtIsSet() =>
        Assert.Equal(InviteStatus.Used, InviteStatusRules.Of(Now.AddDays(-1), Now.AddDays(-2), null, Now));
}
