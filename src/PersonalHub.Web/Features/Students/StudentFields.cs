using System.ComponentModel.DataAnnotations;
using System.Globalization;
using PersonalHub.Web.Domain.Students;

namespace PersonalHub.Web.Features.Students;

/// <summary>Dados do aluno já validados e normalizados.</summary>
public sealed record StudentData(string Name, string Email, string Phone, decimal LessonPrice, short BillingDueDay);

/// <summary>
/// Validação dos campos do aluno, a mesma no cadastro e na edição. As chaves dos erros são os nomes das propriedades
/// dos formulários (Name, Email, Phone, LessonPrice, BillingDueDay).
/// </summary>
public static class StudentFields
{
    public const int NameMaxLength = 150;
    public const int EmailMaxLength = 256;

    public const string DuplicateEmailIndex = "IX_PersonalStudent_PersonalId_StudentEmail";

    public static Dictionary<string, string> Validate(
        string? name, string? email, string? phone, string? lessonPrice, string? billingDueDay, out StudentData data)
    {
        var errors = new Dictionary<string, string>();

        var normalizedName = CollapseSpaces(name);
        if (normalizedName.Length == 0)
        {
            errors["Name"] = "Informe o nome do aluno.";
        }
        else if (normalizedName.Length > NameMaxLength)
        {
            errors["Name"] = $"Use no máximo {NameMaxLength} caracteres.";
        }

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail.Length == 0)
        {
            errors["Email"] = "Informe o e-mail do aluno.";
        }
        else if (normalizedEmail.Length > EmailMaxLength || !new EmailAddressAttribute().IsValid(normalizedEmail)
                 || normalizedEmail.Contains(' '))
        {
            errors["Email"] = "Informe um e-mail válido, como aluno@exemplo.com.";
        }

        var e164 = BrazilianPhone.ToE164(phone);
        if (e164 is null)
        {
            errors["Phone"] = "Informe um celular com DDD.";
        }

        var price = LessonPrice.TryParse(lessonPrice);
        if (string.IsNullOrWhiteSpace(lessonPrice))
        {
            errors["LessonPrice"] = "Informe o valor da aula.";
        }
        else if (price is null || !LessonPrice.IsAllowed(price.Value))
        {
            errors["LessonPrice"] = "Informe um valor entre 0,01 e 99.999,99.";
        }

        if (!short.TryParse(billingDueDay, NumberStyles.None, CultureInfo.InvariantCulture, out var dueDay)
            || dueDay is < 1 or > 31)
        {
            errors["BillingDueDay"] = "Escolha o dia do vencimento.";
        }

        data = new StudentData(normalizedName, normalizedEmail, e164 ?? string.Empty, price ?? 0, dueDay);
        return errors;
    }

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static string CollapseSpaces(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
