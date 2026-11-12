using System.Globalization;

namespace PersonalHub.Web.Features.PhoneVerification;

/// <summary>Textos das telas de código (convite, confirmação de celular e nova senha).</summary>
public static class PhoneCodeDisplay
{
    public const string Consent = "Ao continuar, você aceita receber pelo WhatsApp os avisos das suas aulas e pagamentos.";
    public const string Expired = "Esse código expirou.";
    public const string Invalid = "Digite os 6 números do código.";

    public static string Wrong(int remaining) =>
        $"Código incorreto. Você ainda tem {remaining} {(remaining == 1 ? "tentativa" : "tentativas")}.";

    /// <summary>"Muitas tentativas. Peça um código novo daqui a 15 minutos."</summary>
    public static string Locked(DateTime untilUtc, DateTime nowUtc)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((untilUtc - nowUtc).TotalMinutes));
        return $"Muitas tentativas. Peça um código novo daqui a {minutes} {(minutes == 1 ? "minuto" : "minutos")}.";
    }

    /// <summary>Segundos até poder reenviar (0 = já pode).</summary>
    public static int SecondsUntil(DateTime? atUtc, DateTime nowUtc) =>
        atUtc is { } at && at > nowUtc ? (int)Math.Ceiling((at - nowUtc).TotalSeconds) : 0;

    /// <summary>"0:45".</summary>
    public static string Countdown(int seconds) =>
        $"{seconds / 60}:{(seconds % 60).ToString("D2", CultureInfo.InvariantCulture)}";

    /// <summary>Primeiro nome do personal para "WhatsApp cadastrado pelo Carlos".</summary>
    public static string FirstName(string name) => Domain.Notifications.NotificationTemplates.FirstName(name);
}
