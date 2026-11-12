namespace PersonalHub.Web.Infrastructure.WhatsApp;

/// <summary>Telefone para log: só os 4 últimos dígitos ("+55•••••••2468").</summary>
public static class PhoneLogMask
{
    public static string Mask(string? e164) =>
        string.IsNullOrEmpty(e164) || e164.Length <= 4
            ? "••••"
            : (e164.StartsWith('+') ? e164[..3] : string.Empty) + new string('•', Math.Max(0, e164.Length - 7)) + e164[^4..];
}
