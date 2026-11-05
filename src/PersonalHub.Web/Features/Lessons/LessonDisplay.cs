using System.Globalization;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Features.Students;

namespace PersonalHub.Web.Features.Lessons;

/// <summary>Rótulos (D-038) e formatos brasileiros usados nas telas de aulas.</summary>
public static class LessonDisplay
{
    private static CultureInfo PtBr => StudentDisplay.PtBr;

    public static string StatusLabel(LessonStatus status, bool deleted) => deleted
        ? "Excluída"
        : status switch
        {
            LessonStatus.Approved => "Confirmada",
            LessonStatus.Rejected => "Recusada",
            _ => "Aguardando"
        };

    public static string StatusPillClass(LessonStatus status, bool deleted) => deleted
        ? "ph-pill-muted"
        : status switch
        {
            LessonStatus.Approved => "ph-pill-success",
            LessonStatus.Rejected => "ph-pill-danger",
            _ => "ph-pill-pending"
        };

    /// <summary>"Novembro de 2026".</summary>
    public static string Month(DateOnly month) => Capitalize(month.ToString("MMMM 'de' yyyy", PtBr));

    /// <summary>"Hoje, quarta 4" ou "Terça 3".</summary>
    public static string DayHeading(DateOnly day, DateOnly today) =>
        day == today ? $"Hoje, {Weekday(day)} {day.Day}" : $"{Capitalize(Weekday(day))} {day.Day}";

    /// <summary>"Quarta-feira, 4 de novembro de 2026".</summary>
    public static string LongDate(DateTime utc) =>
        Capitalize(SaoPaulo.ToLocalDate(utc).ToString("dddd, d 'de' MMMM 'de' yyyy", PtBr));

    /// <summary>"Terça, 3 de novembro".</summary>
    public static string ShortDate(DateTime utc)
    {
        var day = SaoPaulo.ToLocalDate(utc);
        return $"{Capitalize(Weekday(day))}, {day.ToString("d 'de' MMMM", PtBr)}";
    }

    public static string Time(DateTime utc) => SaoPaulo.ToLocal(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DayMonth(DateTime utc) => StudentDisplay.DayMonth(utc);

    public static string Date(DateTime utc) => StudentDisplay.Date(utc);

    /// <summary>"04/11 às 14:30".</summary>
    public static string DayMonthTime(DateTime utc) => $"{DayMonth(utc)} às {Time(utc)}";

    public static string Duration(short? minutes) => minutes is { } m ? $"{m} min" : "Não informada";

    /// <summary>"60 min · R$ 80,00", ou só o valor quando a duração não foi informada.</summary>
    public static string Summary(short? minutes, decimal unitPrice) =>
        minutes is { } m ? $"{m} min · {StudentDisplay.Money(unitPrice)}" : StudentDisplay.Money(unitPrice);

    /// <summary>"Ana", "Ana e Bruno", "Ana, Bruno e Carla".</summary>
    public static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " e " + names[^1]
    };

    /// <summary>"quarta" em vez de "quarta-feira".</summary>
    private static string Weekday(DateOnly day) => day.ToString("dddd", PtBr).Replace("-feira", string.Empty, StringComparison.Ordinal);

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], PtBr) + text[1..];
}
