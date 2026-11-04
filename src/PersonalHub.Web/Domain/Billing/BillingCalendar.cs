namespace PersonalHub.Web.Domain.Billing;

public static class BillingCalendar
{
    /// <summary>
    /// Data de vencimento no mês informado. Se o dia não existir no mês, usa o último dia (D-025).
    /// </summary>
    public static DateOnly DueDateIn(int year, int month, int billingDueDay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(billingDueDay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(billingDueDay, 31);

        var day = Math.Min(billingDueDay, DateTime.DaysInMonth(year, month));
        return new DateOnly(year, month, day);
    }
}
