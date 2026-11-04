using System.Diagnostics;

namespace PersonalHub.Web.Infrastructure.Observability;

public static class CorrelationId
{
    public const string HeaderName = "X-Correlation-ID";
    public const int MaxLength = 64;

    /// <summary>
    /// Aceita somente letras, dígitos e hífen, até 64 caracteres. O valor vem do cliente e vai para logs e
    /// headers, então qualquer outro formato é descartado (evita injeção em logs e headers).
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Usa o valor recebido se for válido; senão o TraceId atual; senão um novo Guid.</summary>
    public static string Resolve(string? incoming, Activity? activity)
    {
        if (IsValid(incoming))
        {
            return incoming!;
        }

        return activity is not null && activity.TraceId != default
            ? activity.TraceId.ToHexString()
            : Guid.NewGuid().ToString("N");
    }
}
