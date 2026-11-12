using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Features.Notifications;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Lessons.DeleteLesson;

public enum DeleteLessonOutcome
{
    Succeeded,

    /// <summary>Aula de outro personal, inexistente, ou personal inativo.</summary>
    NotFound,

    /// <summary>A aula já foi excluída (duplo toque, outra aba).</summary>
    AlreadyDeleted,

    /// <summary>A aula já foi respondida e o motivo veio vazio.</summary>
    ReasonRequired,

    ReasonTooLong
}

/// <summary>
/// Exclusão lógica pelo personal (D-042). O motivo é obrigatório quando o aluno já respondeu, e a regra é conferida
/// com a aula travada: se o aluno respondeu enquanto a confirmação estava aberta, o pedido sem motivo volta pedindo um.
/// </summary>
public sealed partial class DeleteLessonHandler(AppDbContext db, TimeProvider clock, ILogger<DeleteLessonHandler> logger)
{
    public async Task<DeleteLessonOutcome> HandleAsync(
        Guid personalUserId, Guid lessonId, string? reason, string? correlationId,
        CancellationToken cancellationToken)
    {
        var text = LessonRules.NormalizeText(reason);
        if (text is { Length: > LessonRules.TextMaxLength })
        {
            return DeleteLessonOutcome.ReasonTooLong;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lesson = await db.LockForPersonalAsync(personalUserId, lessonId, cancellationToken);
        if (lesson is null)
        {
            return DeleteLessonOutcome.NotFound;
        }

        if (lesson.IsDeleted)
        {
            return DeleteLessonOutcome.AlreadyDeleted;
        }

        if (lesson.DeletionRequiresReason && text is null)
        {
            return DeleteLessonOutcome.ReasonRequired;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var status = lesson.Status;
        lesson.Delete(personalUserId, text, now);
        db.AddLessonAudit(personalUserId, "LessonDeleted", lesson.Id, now, correlationId,
            new { status = status.ToString(), hasReason = text is not null });
        await db.LessonDeletedAsync(lesson, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogDeleted(logger, lesson.Id, personalUserId, status);
        return DeleteLessonOutcome.Succeeded;
    }

    [LoggerMessage(EventId = 4010, Level = LogLevel.Information,
        Message = "Aula {LessonId} excluída pelo personal {UserId}; situação {Status}")]
    private static partial void LogDeleted(ILogger logger, Guid lessonId, Guid userId, LessonStatus status);
}
