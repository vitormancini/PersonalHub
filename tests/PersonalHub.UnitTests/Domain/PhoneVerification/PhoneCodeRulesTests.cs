using PersonalHub.Web.Domain.PhoneVerification;

namespace PersonalHub.UnitTests.Domain.PhoneVerification;

public class PhoneCodeRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CanSend_WithoutHistory_IsAllowed()
    {
        Assert.Equal(SendCheck.Allowed, PhoneCodeRules.CanSend([], null, Now, out _));
    }

    [Fact]
    public void CanSend_Within60Seconds_IsCooldownUntilTheMinuteEnds()
    {
        var last = Now.AddSeconds(-20);

        Assert.Equal(SendCheck.Cooldown, PhoneCodeRules.CanSend([last], null, Now, out var retryAt));
        Assert.Equal(last.AddSeconds(60), retryAt);
    }

    [Fact]
    public void CanSend_After60Seconds_IsAllowed()
    {
        Assert.Equal(SendCheck.Allowed, PhoneCodeRules.CanSend([Now.AddSeconds(-60)], null, Now, out _));
    }

    [Fact]
    public void CanSend_FourthInTheHour_LocksFor15Minutes()
    {
        DateTime[] sends = [Now.AddMinutes(-50), Now.AddMinutes(-20), Now.AddMinutes(-2)];

        Assert.Equal(SendCheck.Locked, PhoneCodeRules.CanSend(sends, null, Now, out var retryAt));
        Assert.Equal(Now.AddMinutes(15), retryAt);
    }

    [Fact]
    public void CanSend_SendsOlderThanAnHour_DoNotCount()
    {
        DateTime[] sends = [Now.AddMinutes(-70), Now.AddMinutes(-65), Now.AddMinutes(-61)];

        Assert.Equal(SendCheck.Allowed, PhoneCodeRules.CanSend(sends, null, Now, out _));
    }

    [Fact]
    public void CanSend_WhileLocked_IsLockedUntilTheEnd()
    {
        var until = Now.AddMinutes(9);

        Assert.Equal(SendCheck.Locked, PhoneCodeRules.CanSend([], until, Now, out var retryAt));
        Assert.Equal(until, retryAt);
    }

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData(" 123 456 ", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData("12345", null)]
    [InlineData("1234567", null)]
    [InlineData("12a456", null)]
    [InlineData("١٢٣٤٥٦", null)] // dígitos não ASCII
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_KeepsOnlySixAsciiDigits(string? input, string? expected)
    {
        Assert.Equal(expected, PhoneCodeRules.Normalize(input));
    }

    [Theory]
    [InlineData("+5511987652468", "(11) 9••••-2468")]
    [InlineData("+551123452468", "(11) ••••-2468")]
    [InlineData("+14155552468", "••••••••2468")]
    public void Mask_ShowsOnlyDddAndLastDigits(string e164, string expected)
    {
        Assert.Equal(expected, PhoneCodeRules.Mask(e164));
    }

    [Fact]
    public void Code_IsOpenOnlyBeforeExpiryAndUnused()
    {
        var code = new PhoneVerificationCode { PhoneE164 = "+5511987652468", CodeHash = new byte[32], ExpiresAtUtc = Now.AddMinutes(1) };
        Assert.True(code.IsOpen(Now));
        Assert.False(code.IsOpen(Now.AddMinutes(1)));

        code.ConsumedAtUtc = Now;
        Assert.False(code.IsOpen(Now));

        code.ConsumedAtUtc = null;
        code.ReplacedAtUtc = Now;
        Assert.False(code.IsOpen(Now));
    }
}
