using System.Globalization;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Features.Lessons;
using PersonalHub.Web.Features.Students;

namespace PersonalHub.Web.Features.Billing;

/// <summary>Rótulos (D-048, D-049) e formatos das telas de demonstrativo e pagamento.</summary>
public static class BillingDisplay
{
    private static CultureInfo PtBr => StudentDisplay.PtBr;

    public static string StatusLabel(BillingPeriodStatus status) => status switch
    {
        BillingPeriodStatus.Paid => "Pago",
        BillingPeriodStatus.PaymentReported => "Pagamento informado",
        _ => "Aguardando pagamento"
    };

    public static string StatusPillClass(BillingPeriodStatus status) => status switch
    {
        BillingPeriodStatus.Paid => "ph-pill-success",
        BillingPeriodStatus.PaymentReported => "ph-pill-info",
        _ => "ph-pill-pending"
    };

    /// <summary>"Outubro".</summary>
    public static string MonthName(DateOnly month) => Capitalize(month.ToString("MMMM", PtBr));

    /// <summary>"outubro".</summary>
    public static string MonthNameLower(DateOnly month) => month.ToString("MMMM", PtBr);

    /// <summary>"Outubro de 2026".</summary>
    public static string MonthYear(DateOnly month) => LessonDisplay.Month(month);

    public static string DayMonth(DateOnly day) => day.ToString("dd/MM", CultureInfo.InvariantCulture);

    public static string Date(DateOnly day) => day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>"05/11 às 14:30" (horário de Brasília).</summary>
    public static string DayMonthTime(DateTime utc) => LessonDisplay.DayMonthTime(utc);

    /// <summary>"05/11" (data de Brasília).</summary>
    public static string DayMonth(DateTime utc) => LessonDisplay.DayMonth(utc);

    public static string Lessons(int count) => count == 1 ? "1 aula" : $"{count} aulas";

    public static string Money(decimal value) => StudentDisplay.Money(value);

    /// <summary>"vence hoje", "vence dia 10" ou "venceu dia 5".</summary>
    public static string DueText(DateOnly dueDate, DateOnly today) =>
        dueDate == today ? "vence hoje" : dueDate > today ? $"vence dia {dueDate.Day}" : $"venceu dia {dueDate.Day}";

    /// <summary>Badge do aluno: "Vence hoje" ou "Venceu em 05/11".</summary>
    public static string StudentDueBadge(DateOnly dueDate, DateOnly today) =>
        dueDate >= today ? (dueDate == today ? "Vence hoje" : $"Vence em {DayMonth(dueDate)}") : $"Venceu em {DayMonth(dueDate)}";

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], PtBr) + text[1..];
}
