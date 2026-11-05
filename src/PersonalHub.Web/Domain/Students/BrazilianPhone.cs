namespace PersonalHub.Web.Domain.Students;

/// <summary>
/// Celular brasileiro digitado pelo personal, nos formatos usuais: "(11) 99999-8888", "11 999998888",
/// "+55 11 99999-8888", "011 99999-8888". Sempre exige o DDD.
/// </summary>
public static class BrazilianPhone
{
    /// <summary>
    /// Normaliza para E.164 (+55 + DDD + número). Aceita 11 dígitos nacionais começando por 9 (celular) ou
    /// 10 dígitos de fixo (WhatsApp Business também roda em fixo). Retorna nulo quando não reconhece o número.
    /// </summary>
    public static string? ToE164(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        var international = text.StartsWith('+');
        var digits = new List<char>(16);
        foreach (var c in international ? text[1..] : text)
        {
            if (char.IsAsciiDigit(c))
            {
                digits.Add(c);
            }
            else if (c is not (' ' or '-' or '(' or ')' or '.'))
            {
                return null;
            }
        }

        var number = new string(digits.ToArray());
        if (international)
        {
            if (!number.StartsWith("55", StringComparison.Ordinal))
            {
                return null;
            }

            number = number[2..];
        }
        else if (number.Length is 12 or 13 && number.StartsWith("55", StringComparison.Ordinal))
        {
            number = number[2..];
        }
        else if (number.Length is 11 or 12 && number[0] == '0')
        {
            // Prefixo de discagem interurbana (0 + DDD).
            number = number[1..];
        }

        return IsNationalNumber(number) ? "+55" + number : null;
    }

    /// <summary>"+5511987654321" vira "(11) 98765-4321"; fixo vira "(11) 2345-6789". Outros formatos voltam como estão.</summary>
    public static string ToDisplay(string e164)
    {
        if (!e164.StartsWith("+55", StringComparison.Ordinal) || !IsNationalNumber(e164[3..]))
        {
            return e164;
        }

        var number = e164[3..];
        var split = number.Length - 4;
        return $"({number[..2]}) {number[2..split]}-{number[split..]}";
    }

    private static bool IsNationalNumber(string number)
    {
        // DDDs válidos vão de 11 a 99 e não têm zero.
        if (number.Length is not (10 or 11) || number[0] == '0' || number[1] == '0')
        {
            return false;
        }

        var subscriber = number[2];
        return number.Length == 11 ? subscriber == '9' : subscriber is >= '2' and <= '8';
    }
}
