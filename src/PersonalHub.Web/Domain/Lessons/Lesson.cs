namespace PersonalHub.Web.Domain.Lessons;

/// <summary>
/// Aula registrada pelo personal. A exclusão é lógica (<see cref="DeletedAtUtc"/>) e fica separada
/// da decisão do aluno (<see cref="Status"/>). A aula não é editável: correção é excluir e registrar outra (D-041).
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

    public bool IsDeleted => DeletedAtUtc is not null;

    /// <summary>O aluno responde uma única vez, e só enquanto a aula está pendente e não excluída (D-041).</summary>
    public bool CanRespond => Status == LessonStatus.Pending && !IsDeleted;

    /// <summary>Excluir aula já respondida exige motivo, que o aluno vê (D-042).</summary>
    public bool DeletionRequiresReason => Status != LessonStatus.Pending;

    public void Approve(Guid studentUserId, DateTime nowUtc)
    {
        EnsureCanRespond();
        Status = LessonStatus.Approved;
        RespondedAtUtc = nowUtc;
        RespondedByUserId = studentUserId;
    }

    public void Reject(Guid studentUserId, string reason, DateTime nowUtc)
    {
        EnsureCanRespond();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A recusa exige motivo (D-021).", nameof(reason));
        }

        Status = LessonStatus.Rejected;
        RejectionReason = reason;
        RespondedAtUtc = nowUtc;
        RespondedByUserId = studentUserId;
    }

    public void Delete(Guid personalUserId, string? reason, DateTime nowUtc)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("A aula já foi excluída.");
        }

        if (DeletionRequiresReason && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Excluir aula já respondida exige motivo (D-042).", nameof(reason));
        }

        DeletedAtUtc = nowUtc;
        DeletedByUserId = personalUserId;
        DeletionReason = string.IsNullOrWhiteSpace(reason) ? null : reason;
    }

    private void EnsureCanRespond()
    {
        if (!CanRespond)
        {
            throw new InvalidOperationException("Só uma aula pendente e não excluída pode ser respondida.");
        }
    }
}
