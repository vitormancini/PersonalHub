using System.Globalization;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Lessons;

namespace PersonalHub.Web.Features.Lessons.RegisterLesson;

public sealed record RegisterLessonData(Guid PersonalStudentId, DateTime OccurredAtUtc, short? DurationMinutes, string? Notes);

/// <summary>Validação dos campos de "Registrar aula" (D-039). Mensagens prontas para a tela, por propriedade.</summary>
public static class RegisterLessonFields
{
    public const string FutureMessage = "A aula não pode ser no futuro. Registre depois de dar a aula.";

    private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss"];

    public static Dictionary<string, string> Validate(RegisterLessonInput input, DateTime nowUtc, out RegisterLessonData data)
    {
        var errors = new Dictionary<string, string>();
        data = default!;

        if (!Guid.TryParse(input.StudentId, out var personalStudentId))
        {
            errors[nameof(input.StudentId)] = "Escolha o aluno.";
        }

        var hasDate = DateOnly.TryParseExact(input.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date);
        if (!hasDate)
        {
            errors[nameof(input.Date)] = "Informe a data da aula.";
        }

        var hasTime = TimeOnly.TryParseExact(input.Time?.Trim(), TimeFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var time);
        if (!hasTime)
        {
            errors[nameof(input.Time)] = "Informe o horário da aula.";
        }

        DateTime occurredAtUtc = default;
        if (hasDate && hasTime)
        {
            // O formulário registra até o minuto.
            var utc = SaoPaulo.ToUtc(date, new TimeOnly(time.Hour, time.Minute));
            if (utc is null)
            {
                errors[nameof(input.Time)] = "Este horário não existe no horário de Brasília.";
            }
            else if (LessonRules.IsInFuture(utc.Value, nowUtc))
            {
                errors[nameof(input.Date)] = FutureMessage;
            }
            else
            {
                occurredAtUtc = utc.Value;
            }
        }

        short? duration = null;
        if (!string.IsNullOrWhiteSpace(input.Duration))
        {
            if (short.TryParse(input.Duration.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
                && LessonRules.IsAllowedDuration(minutes))
            {
                duration = minutes;
            }
            else
            {
                errors[nameof(input.Duration)] = "Escolha 30, 45, 60 ou 90 minutos.";
            }
        }

        var notes = LessonRules.NormalizeText(input.Notes);
        if (notes is { Length: > LessonRules.TextMaxLength })
        {
            errors[nameof(input.Notes)] = $"A observação pode ter até {LessonRules.TextMaxLength} caracteres.";
        }

        if (errors.Count == 0)
        {
            data = new RegisterLessonData(personalStudentId, occurredAtUtc, duration, notes);
        }

        return errors;
    }

    /// <summary>Identifica aluno + data + horário de uma possível duplicada (D-040).</summary>
    public static string DuplicateKey(Guid personalStudentId, DateTime occurredAtUtc) =>
        $"{personalStudentId:N}|{occurredAtUtc.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture)}";
}
