namespace PersonalHub.Web.Domain;

/// <summary>Fuso do produto (D-026). Instantes ficam em UTC; datas mostradas ao usuário, no horário de Brasília.</summary>
public static class SaoPaulo
{
    public static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateOnly ToLocalDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone);

    /// <summary>
    /// Data e hora de São Paulo em UTC. Nulo quando o horário não existiu (pulado no início de um antigo horário de
    /// verão).
    /// </summary>
    public static DateTime? ToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        return TimeZone.IsInvalidTime(local) ? null : TimeZoneInfo.ConvertTimeToUtc(local, TimeZone);
    }

    /// <summary>Início (inclusivo) e fim (exclusivo) do mês de São Paulo, em UTC.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) MonthRangeUtc(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        return (StartOfDayUtc(start), StartOfDayUtc(start.AddMonths(1)));
    }

    /// <summary>Início do dia de São Paulo, em UTC.</summary>
    /// <remarks>Meia-noite pode não existir num dia de início de horário de verão (até 2019): aí o dia começa à 01:00.</remarks>
    public static DateTime StartOfDayUtc(DateOnly date) =>
        ToUtc(date, TimeOnly.MinValue) ?? ToUtc(date, new TimeOnly(1, 0))!.Value;
}
