namespace PersonalHub.Web.Domain.Notifications;

/// <summary>Quando enviar: horário de silêncio e intervalo entre tentativas (D-061, D-062).</summary>
public static class NotificationSchedule
{
    public const int MaxAttempts = 5;

    /// <summary>Silêncio das 22:00 às 08:00 de São Paulo.</summary>
    public static readonly TimeOnly QuietStart = new(22, 0);
    public static readonly TimeOnly QuietEnd = new(8, 0);

    // Espera depois da 1ª, 2ª, 3ª e 4ª falha. A 5ª falha encerra como Failed.
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2)
    ];

    /// <summary>
    /// O instante em que o aviso pode sair. Dentro do silêncio, empurra para 08:00 de São Paulo. Códigos de verificação
    /// sempre saem na hora.
    /// </summary>
    public static DateTime SendAt(DateTime desiredUtc, bool isAuthentication)
    {
        if (isAuthentication)
        {
            return desiredUtc;
        }

        var local = SaoPaulo.ToLocal(desiredUtc);
        var time = TimeOnly.FromDateTime(local);
        var date = DateOnly.FromDateTime(local);
        if (time >= QuietStart)
        {
            return MorningUtc(date.AddDays(1));
        }

        return time < QuietEnd ? MorningUtc(date) : desiredUtc;
    }

    /// <summary>Espera antes da próxima tentativa, ou nulo quando <paramref name="attempts"/> já esgotou o limite.</summary>
    public static TimeSpan? RetryDelay(int attempts) =>
        attempts is >= 1 and < MaxAttempts ? Backoff[attempts - 1] : null;

    private static DateTime MorningUtc(DateOnly date) => SaoPaulo.ToUtc(date, QuietEnd)!.Value;
}
