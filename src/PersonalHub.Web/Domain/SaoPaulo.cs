namespace PersonalHub.Web.Domain;

/// <summary>Fuso do produto (D-026). Instantes ficam em UTC; datas mostradas ao usuário, no horário de Brasília.</summary>
public static class SaoPaulo
{
    public static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateOnly ToLocalDate(DateTime utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZone));
}
