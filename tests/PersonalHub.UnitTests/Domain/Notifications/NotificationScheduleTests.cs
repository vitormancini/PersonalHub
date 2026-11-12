using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Notifications;

namespace PersonalHub.UnitTests.Domain.Notifications;

public class NotificationScheduleTests
{
    private static DateTime Sp(int day, int hour, int minute = 0) =>
        SaoPaulo.ToUtc(new DateOnly(2026, 11, day), new TimeOnly(hour, minute))!.Value;

    [Theory]
    [InlineData(12, 8, 0)]
    [InlineData(12, 14, 30)]
    [InlineData(12, 21, 59)]
    public void SendAt_OutsideQuietHours_IsImmediate(int day, int hour, int minute)
    {
        Assert.Equal(Sp(day, hour, minute), NotificationSchedule.SendAt(Sp(day, hour, minute), isAuthentication: false));
    }

    [Theory]
    [InlineData(12, 22, 0)]
    [InlineData(12, 23, 45)]
    public void SendAt_LateEvening_MovesToNextMorning(int day, int hour, int minute)
    {
        Assert.Equal(Sp(day + 1, 8), NotificationSchedule.SendAt(Sp(day, hour, minute), isAuthentication: false));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 10)]
    [InlineData(7, 59)]
    public void SendAt_EarlyMorning_MovesToSameMorning(int hour, int minute)
    {
        Assert.Equal(Sp(13, 8), NotificationSchedule.SendAt(Sp(13, hour, minute), isAuthentication: false));
    }

    [Theory]
    [InlineData(23, 0)]
    [InlineData(3, 0)]
    public void SendAt_Codes_IgnoreQuietHours(int hour, int minute)
    {
        Assert.Equal(Sp(13, hour, minute), NotificationSchedule.SendAt(Sp(13, hour, minute), isAuthentication: true));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 30)]
    [InlineData(4, 120)]
    public void RetryDelay_GrowsBetweenAttempts(int attempts, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), NotificationSchedule.RetryDelay(attempts));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(6)]
    public void RetryDelay_AfterFifthAttempt_GivesUp(int attempts)
    {
        Assert.Null(NotificationSchedule.RetryDelay(attempts));
    }
}
