namespace PersonalHub.Web.Features.Lessons.RegisterLesson;

/// <summary>Campos do formulário "Registrar aula", como texto. O handler valida e converte tudo no servidor.</summary>
public sealed class RegisterLessonInput
{
    public string? StudentId { get; set; }

    /// <summary>yyyy-MM-dd, como o input type=date envia.</summary>
    public string? Date { get; set; }

    /// <summary>HH:mm (ou HH:mm:ss), como o input type=time envia.</summary>
    public string? Time { get; set; }

    public string? Duration { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Chave da possível duplicada que o personal viu e decidiu registrar mesmo assim (D-040). Só vale para o mesmo
    /// aluno, data e horário: se ele mudar algum dos três, o aviso volta a aparecer se houver outra coincidência.
    /// </summary>
    public string? ConfirmedDuplicate { get; set; }
}
