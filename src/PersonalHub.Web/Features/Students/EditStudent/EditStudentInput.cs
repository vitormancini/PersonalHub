namespace PersonalHub.Web.Features.Students.EditStudent;

/// <summary>Campos do formulário "Editar aluno", como texto. O handler valida e converte tudo no servidor.</summary>
public sealed class EditStudentInput
{
    public string? Name { get; set; }

    /// <summary>Ignorado quando o vínculo não está mais aguardando aceite (D-035).</summary>
    public string? Email { get; set; }

    public string? Phone { get; set; }
    public string? LessonPrice { get; set; }
    public string? BillingDueDay { get; set; }

    /// <summary>Valor de <c>xmin</c> lido ao abrir a tela, para detectar alteração salva por outra aba ou pessoa.</summary>
    public string? Version { get; set; }
}
