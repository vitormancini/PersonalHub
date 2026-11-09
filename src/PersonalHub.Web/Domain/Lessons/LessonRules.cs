namespace PersonalHub.Web.Domain.Lessons;

/// <summary>Regras do registro de aula (D-039).</summary>
public static class LessonRules
{
    /// <summary>Limite de observação, motivo de recusa e motivo de exclusão.</summary>
    public const int TextMaxLength = 500;

    public static readonly IReadOnlyList<short> Durations = [30, 45, 60, 90];

    public static bool IsAllowedDuration(short? minutes) => minutes is null || Durations.Contains(minutes.Value);

    /// <summary>A aula não pode ser no futuro; retroativa é permitida.</summary>
    public static bool IsInFuture(DateTime occurredAtUtc, DateTime nowUtc) => occurredAtUtc > nowUtc;

    /// <summary>Texto livre (observação ou motivo) aparado; nulo quando vazio.</summary>
    public static string? NormalizeText(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
