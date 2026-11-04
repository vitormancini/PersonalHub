using PersonalHub.Web.Domain.Billing;

namespace PersonalHub.UnitTests.Domain.Billing;

public class BillingCalendarTests
{
    [Theory]
    [InlineData(2027, 1, 15, 15)]
    [InlineData(2027, 1, 31, 31)]
    [InlineData(2027, 2, 31, 28)]
    [InlineData(2028, 2, 31, 29)]
    [InlineData(2028, 2, 29, 29)]
    [InlineData(2027, 4, 31, 30)]
    [InlineData(2027, 2, 1, 1)]
    public void DueDateIn_UsesLastDayWhenDayDoesNotExist(int year, int month, int dueDay, int expectedDay)
    {
        var dueDate = BillingCalendar.DueDateIn(year, month, dueDay);

        Assert.Equal(new DateOnly(year, month, expectedDay), dueDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(-1)]
    public void DueDateIn_RejectsDayOutOfRange(int dueDay)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BillingCalendar.DueDateIn(2027, 1, dueDay));
    }
}
