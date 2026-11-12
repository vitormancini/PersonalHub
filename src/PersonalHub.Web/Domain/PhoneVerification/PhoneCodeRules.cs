namespace PersonalHub.Web.Domain.PhoneVerification;

/// <summary>Resultado da checagem antes de enviar um código.</summary>
public enum SendCheck
{
    Allowed,

    /// <summary>Menos de 60 s desde o último envio.</summary>
    Cooldown,

    /// <summary>Bloqueado por tentativas erradas ou pelo limite de envios por hora.</summary>
    Locked
}

/// <summary>Regras dos códigos por WhatsApp (D-056). Funções puras: o serviço busca o histórico e grava.</summary>
public static class PhoneCodeRules
{
    public const int Digits = 6;
    public const int MaxAttempts = 5;
    public const int MaxSendsPerHour = 3;
    public static readonly TimeSpan Validity = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendAfter = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Pode enviar outro código para o celular? <paramref name="recentSends"/> são os envios da última hora (qualquer
    /// ordem); <paramref name="lockedUntilUtc"/> é o maior bloqueio gravado. <paramref name="retryAtUtc"/> diz quando
    /// tentar de novo.
    /// </summary>
    public static SendCheck CanSend(
        IReadOnlyCollection<DateTime> recentSends, DateTime? lockedUntilUtc, DateTime nowUtc, out DateTime retryAtUtc)
    {
        retryAtUtc = nowUtc;
        if (lockedUntilUtc > nowUtc)
        {
            retryAtUtc = lockedUntilUtc.Value;
            return SendCheck.Locked;
        }

        var window = recentSends.Where(s => s > nowUtc - SendWindow).ToList();
        if (window.Count > 0 && window.Max() + ResendAfter > nowUtc)
        {
            retryAtUtc = window.Max() + ResendAfter;
            return SendCheck.Cooldown;
        }

        if (window.Count >= MaxSendsPerHour)
        {
            // Pediu o 4º código na mesma hora: bloqueia por 15 minutos (o serviço grava o bloqueio).
            retryAtUtc = nowUtc + LockDuration;
            return SendCheck.Locked;
        }

        return SendCheck.Allowed;
    }

    /// <summary>Código só com dígitos, depois de tirar espaços e traços que o teclado ou a colagem trazem.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return digits.Length == Digits && digits.All(char.IsAsciiDigit) ? digits : null;
    }

    /// <summary>"(11) 9••••-2468": mostra o DDD, o primeiro dígito do celular e os 4 últimos.</summary>
    public static string Mask(string e164)
    {
        if (!e164.StartsWith("+55", StringComparison.Ordinal) || e164.Length is not (13 or 14))
        {
            return e164.Length > 4 ? new string('•', e164.Length - 4) + e164[^4..] : "••••";
        }

        var number = e164[3..];
        var ddd = number[..2];
        var last = number[^4..];
        return number.Length == 11 ? $"({ddd}) {number[2]}••••-{last}" : $"({ddd}) ••••-{last}";
    }
}
