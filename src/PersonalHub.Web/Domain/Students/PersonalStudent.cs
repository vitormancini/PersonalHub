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
}
