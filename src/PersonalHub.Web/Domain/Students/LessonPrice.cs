using System.Globalization;

namespace PersonalHub.Web.Domain.Students;

/// <summary>Valor da aula em reais (D-018), digitado no formato brasileiro.</summary>
public static class LessonPrice
{
    public const decimal Max = 99_999.99m;

    public static bool IsAllowed(decimal value) => value > 0 && value <= Max && decimal.Round(value, 2) == value;

    /// <summary>
    /// Lê "80", "80,5", "80,00", "1.234,56", "R$ 1234,56". Com vírgula, os pontos são separadores de milhar
    /// (grupos de 3). Sem vírgula, "1.500" é mil e quinhentos, mas "80.50" (teclado em inglês) vale 80,50.
    /// Retorna nulo quando o texto não é um valor com até 2 casas decimais.
    /// </summary>
    public static decimal? TryParse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Trim();
        if (text.StartsWith("R$", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..].Trim();
        }

        string integerPart;
        string decimalPart;
        var comma = text.IndexOf(',');
        if (comma >= 0)
        {
            integerPart = text[..comma];
            decimalPart = text[(comma + 1)..];
            if (!IsThousandsGrouped(integerPart))
            {
                return null;
            }
        }
        else if (IsThousandsGrouped(text))
        {
            integerPart = text;
            decimalPart = string.Empty;
        }
        else
        {
            var dot = text.IndexOf('.');
            if (dot < 0 || text.IndexOf('.', dot + 1) >= 0)
            {
                return null;
            }

            integerPart = text[..dot];
            decimalPart = text[(dot + 1)..];
        }

        integerPart = integerPart.Replace(".", string.Empty, StringComparison.Ordinal);
        if (integerPart.Length is 0 or > 9 || decimalPart.Length > 2
            || !integerPart.All(char.IsAsciiDigit) || !decimalPart.All(char.IsAsciiDigit)
            || (comma >= 0 && decimalPart.Length == 0))
        {
            return null;
        }

        var normalized = decimalPart.Length == 0 ? integerPart : $"{integerPart}.{decimalPart}";
        return decimal.Parse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    /// <summary>"1234" ou "1.234.567": só dígitos, com pontos opcionais separando grupos de exatamente 3.</summary>
    private static bool IsThousandsGrouped(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        var groups = text.Split('.');
        if (groups.Length == 1)
        {
            return text.All(char.IsAsciiDigit);
        }

        return groups[0].Length is >= 1 and <= 3
            && groups.All(g => g.All(char.IsAsciiDigit))
            && groups.Skip(1).All(g => g.Length == 3);
    }
}
