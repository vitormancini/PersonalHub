namespace PersonalHub.Web.Domain.Students;

/// <summary>
/// Vínculo personal–aluno (D-015). Guarda os dados que o personal digitou; a conta do aluno
/// (<see cref="StudentUserId"/>) só é ligada quando o convite é aceito.
/// </summary>
public class PersonalStudent
{
    public Guid Id { get; set; }
    public Guid PersonalId { get; set; }
    public Guid? StudentUserId { get; set; }
    public required string StudentName { get; set; }

    /// <summary>E-mail normalizado (minúsculas, sem espaços nas pontas).</summary>
    public required string StudentEmail { get; set; }

    /// <summary>Celular em formato E.164, por exemplo +5511999998888.</summary>
    public required string StudentPhone { get; set; }

    public decimal LessonPrice { get; set; }
    public short BillingDueDay { get; set; }
    public PersonalStudentStatus Status { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// O personal só troca o e-mail enquanto o convite não foi aceito (D-035). Depois, o e-mail é o login do aluno.
    /// </summary>
    public bool CanEditEmail => Status == PersonalStudentStatus.Invited;

    /// <summary>Encerra o vínculo sem apagar histórico (D-028, D-037).</summary>
    public void Inactivate(DateTime nowUtc)
    {
        if (Status == PersonalStudentStatus.Inactive)
        {
            throw new InvalidOperationException("O vínculo já está inativo.");
        }

        Status = PersonalStudentStatus.Inactive;
        EndedAtUtc = nowUtc;
    }

    /// <summary>
    /// Reabre o vínculo (D-037): volta para <c>Active</c> se o aluno já tem conta; senão, para <c>Invited</c>,
    /// e o personal precisa gerar um novo convite.
    /// </summary>
    public void Reactivate()
    {
        if (Status != PersonalStudentStatus.Inactive)
        {
            throw new InvalidOperationException("Só um vínculo inativo pode ser reativado.");
        }

        Status = StudentUserId is null ? PersonalStudentStatus.Invited : PersonalStudentStatus.Active;
        EndedAtUtc = null;
    }
}
