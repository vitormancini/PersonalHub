namespace PersonalHub.Web.Domain.Lessons;

/// <summary>
/// Aula registrada pelo personal. A exclusão é lógica (<see cref="DeletedAtUtc"/>) e fica separada
/// da decisão do aluno (<see cref="Status"/>).
/// </summary>
public class Lesson
{
    public Guid Id { get; set; }
    public Guid PersonalStudentId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public short? DurationMinutes { get; set; }
    public string? Notes { get; set; }

    /// <summary>Cópia do preço do vínculo no momento do registro (D-018).</summary>
    public decimal UnitPrice { get; set; }

    public LessonStatus Status { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public Guid? RespondedByUserId { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedByUserId { get; set; }
    public string? DeletionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
