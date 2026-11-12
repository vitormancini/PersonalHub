using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Notifications;
using PersonalHub.Web.Features.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Lessons.RegisterLesson;

public sealed record RegisterLessonCommand(Guid PersonalUserId, RegisterLessonInput Input, string? CorrelationId);

public enum RegisterLessonOutcome
{
    Succeeded,
    Invalid,

    /// <summary>Já existe aula não excluída do vínculo na mesma data e horário; exige confirmação (D-040).</summary>
    PossibleDuplicate,

    /// <summary>O usuário não tem perfil de personal ativo.</summary>
    Forbidden
}

public sealed record RegisterLessonResult(
    RegisterLessonOutcome Outcome,
    IReadOnlyDictionary<string, string> Errors,
    Guid? LessonId = null,
    DateTime? OccurredAtUtc = null,
    string? DuplicateKey = null)
{
    public static RegisterLessonResult Failed(RegisterLessonOutcome outcome, IReadOnlyDictionary<string, string>? errors = null) =>
        new(outcome, errors ?? new Dictionary<string, string>());
}

/// <summary>
/// Registra a aula do personal logado (D-039): fica <c>Pending</c> até o aluno responder e guarda o preço do vínculo
/// naquele momento (D-018). O vínculo é travado com <c>FOR UPDATE</c>: inativar o aluno, mudar o preço ou registrar
/// a mesma aula em outra aba acontece antes ou depois, nunca no meio.
/// </summary>
public sealed partial class RegisterLessonHandler(AppDbContext db, TimeProvider clock, ILogger<RegisterLessonHandler> logger)
{
    public async Task<RegisterLessonResult> HandleAsync(RegisterLessonCommand command, CancellationToken cancellationToken)
    {
        var personalId = await db.ActivePersonalIdAsync(command.PersonalUserId, cancellationToken);
        if (personalId is null)
        {
            return RegisterLessonResult.Failed(RegisterLessonOutcome.Forbidden);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var errors = RegisterLessonFields.Validate(command.Input, now, out var data);
        if (errors.Count > 0)
        {
            return RegisterLessonResult.Failed(RegisterLessonOutcome.Invalid, errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var link = await db.LockOwnedAsync(personalId.Value, data.PersonalStudentId, cancellationToken);
        if (link is null)
        {
            // Id de outro personal ou inexistente: mesma mensagem de quando nada foi escolhido.
            return Invalid(nameof(RegisterLessonInput.StudentId), "Escolha o aluno.");
        }

        if (link.Status == PersonalStudentStatus.Inactive)
        {
            return Invalid(nameof(RegisterLessonInput.StudentId),
                "Este aluno está inativo. Reative o vínculo para registrar aulas.");
        }

        var duplicateKey = RegisterLessonFields.DuplicateKey(link.Id, data.OccurredAtUtc);
        var confirmed = string.Equals(command.Input.ConfirmedDuplicate, duplicateKey, StringComparison.Ordinal);
        if (!confirmed && await db.Lessons.AnyAsync(
                l => l.PersonalStudentId == link.Id && l.OccurredAtUtc == data.OccurredAtUtc && l.DeletedAtUtc == null,
                cancellationToken))
        {
            return new RegisterLessonResult(RegisterLessonOutcome.PossibleDuplicate, new Dictionary<string, string>(),
                OccurredAtUtc: data.OccurredAtUtc, DuplicateKey: duplicateKey);
        }

        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            PersonalStudentId = link.Id,
            CreatedByUserId = command.PersonalUserId,
            OccurredAtUtc = data.OccurredAtUtc,
            DurationMinutes = data.DurationMinutes,
            Notes = data.Notes,
            UnitPrice = link.LessonPrice,
            Status = LessonStatus.Pending,
            CreatedAtUtc = now
        };
        db.Lessons.Add(lesson);
        db.AddLessonAudit(command.PersonalUserId, "LessonRegistered", lesson.Id, now, command.CorrelationId,
            new { personalStudentId = link.Id, unitPrice = lesson.UnitPrice, duplicateConfirmed = confirmed });
        await db.LessonCreatedAsync(lesson, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogRegistered(logger, lesson.Id, link.Id, command.PersonalUserId);
        return new RegisterLessonResult(RegisterLessonOutcome.Succeeded, new Dictionary<string, string>(), lesson.Id,
            lesson.OccurredAtUtc);
    }

    private static RegisterLessonResult Invalid(string field, string message) =>
        RegisterLessonResult.Failed(RegisterLessonOutcome.Invalid, new Dictionary<string, string> { [field] = message });

    [LoggerMessage(EventId = 4000, Level = LogLevel.Information,
        Message = "Aula {LessonId} registrada no vínculo {PersonalStudentId} pelo personal {UserId}")]
    private static partial void LogRegistered(ILogger logger, Guid lessonId, Guid personalStudentId, Guid userId);
}
