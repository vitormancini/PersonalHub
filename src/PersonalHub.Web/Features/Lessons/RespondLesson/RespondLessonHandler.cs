using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Lessons.RespondLesson;

public enum RespondLessonOutcome
{
    Succeeded,

    /// <summary>Aula de outro aluno ou inexistente.</summary>
    NotFound,

    /// <summary>O personal excluiu a aula antes da resposta.</summary>
    Deleted,

    /// <summary>A aula já foi respondida (duplo toque, outra aba). A resposta é definitiva (D-041).</summary>
    AlreadyResponded,

    ReasonRequired,
    ReasonTooLong
}

/// <summary>
/// Resposta do aluno (D-021, D-041): só o aluno do vínculo, só uma vez, só enquanto a aula está pendente e não
/// excluída. A aula é travada com <c>FOR UPDATE</c>, então duas respostas, ou uma resposta e a exclusão pelo personal,
/// acontecem uma depois da outra e a segunda encontra o estado já mudado.
/// </summary>
public sealed partial class RespondLessonHandler(AppDbContext db, TimeProvider clock, ILogger<RespondLessonHandler> logger)
{
    public Task<RespondLessonOutcome> ApproveAsync(
        Guid studentUserId, Guid lessonId, string? correlationId, CancellationToken cancellationToken) =>
        RespondAsync(studentUserId, lessonId, reject: false, null, correlationId, cancellationToken);

    /// <summary>
    /// O motivo é conferido depois do estado da aula: aula excluída ou já respondida tem prioridade sobre o campo vazio.
    /// </summary>
    public Task<RespondLessonOutcome> RejectAsync(
        Guid studentUserId, Guid lessonId, string? reason, string? correlationId, CancellationToken cancellationToken) =>
        RespondAsync(studentUserId, lessonId, reject: true, reason, correlationId, cancellationToken);

    private async Task<RespondLessonOutcome> RespondAsync(
        Guid studentUserId, Guid lessonId, bool reject, string? reason, string? correlationId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lesson = await db.LockForStudentAsync(studentUserId, lessonId, cancellationToken);
        if (lesson is null)
        {
            return RespondLessonOutcome.NotFound;
        }

        if (lesson.IsDeleted)
        {
            return RespondLessonOutcome.Deleted;
        }

        if (!lesson.CanRespond)
        {
            return RespondLessonOutcome.AlreadyResponded;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (reject)
        {
            var text = LessonRules.NormalizeText(reason);
            if (text is null)
            {
                return RespondLessonOutcome.ReasonRequired;
            }

            if (text.Length > LessonRules.TextMaxLength)
            {
                return RespondLessonOutcome.ReasonTooLong;
            }

            lesson.Reject(studentUserId, text, now);
        }
        else
        {
            lesson.Approve(studentUserId, now);
        }

        // Sem o motivo no metadata: é texto livre do aluno e fica só na aula.
        db.AddLessonAudit(studentUserId, lesson.Status == LessonStatus.Approved ? "LessonApproved" : "LessonRejected",
            lesson.Id, now, correlationId);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogResponded(logger, lesson.Id, studentUserId, lesson.Status);
        return RespondLessonOutcome.Succeeded;
    }

    [LoggerMessage(EventId = 4020, Level = LogLevel.Information,
        Message = "Aula {LessonId} respondida pelo aluno {UserId}: {Status}")]
    private static partial void LogResponded(ILogger logger, Guid lessonId, Guid userId, LessonStatus status);
}
