using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Students.CreateStudent;

public sealed record CreateStudentCommand(Guid PersonalUserId, CreateStudentInput Input, string? CorrelationId);

public enum CreateStudentOutcome
{
    Succeeded,
    Invalid,

    /// <summary>O personal já tem um vínculo não inativo com este e-mail.</summary>
    DuplicateEmail,

    /// <summary>O usuário não tem perfil de personal ativo.</summary>
    Forbidden
}

public sealed record CreateStudentResult(
    CreateStudentOutcome Outcome,
    IReadOnlyDictionary<string, string> Errors,
    Guid? PersonalStudentId = null,
    string? InviteToken = null)
{
    public static CreateStudentResult Failed(CreateStudentOutcome outcome, IReadOnlyDictionary<string, string>? errors = null) =>
        new(outcome, errors ?? new Dictionary<string, string>());
}

/// <summary>
/// Cadastra o aluno do personal logado (D-015/D-016): vínculo em <c>Invited</c>, convite de 7 dias e auditoria
/// numa única transação. Devolve o token puro uma única vez; ele nunca é gravado nem registrado em log.
/// </summary>
public sealed partial class CreateStudentHandler(AppDbContext db, TimeProvider clock, ILogger<CreateStudentHandler> logger)
{
    public async Task<CreateStudentResult> HandleAsync(CreateStudentCommand command, CancellationToken cancellationToken)
    {
        var personalId = await db.PersonalProfiles
            .Where(p => p.UserId == command.PersonalUserId && p.IsActive)
            .Select(p => (Guid?)p.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (personalId is null)
        {
            LogForbidden(logger, command.PersonalUserId);
            return CreateStudentResult.Failed(CreateStudentOutcome.Forbidden);
        }

        var input = command.Input;
        var errors = StudentFields.Validate(
            input.Name, input.Email, input.Phone, input.LessonPrice, input.BillingDueDay, out var data);
        if (errors.Count > 0)
        {
            return CreateStudentResult.Failed(CreateStudentOutcome.Invalid, errors);
        }

        var duplicate = await db.PersonalStudents.AnyAsync(
            s => s.PersonalId == personalId && s.StudentEmail == data.Email && s.Status != PersonalStudentStatus.Inactive,
            cancellationToken);
        if (duplicate)
        {
            return CreateStudentResult.Failed(CreateStudentOutcome.DuplicateEmail);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var link = new PersonalStudent
        {
            Id = Guid.NewGuid(),
            PersonalId = personalId.Value,
            StudentName = data.Name,
            StudentEmail = data.Email,
            StudentPhone = data.Phone,
            LessonPrice = data.LessonPrice,
            BillingDueDay = data.BillingDueDay,
            Status = PersonalStudentStatus.Invited,
            CreatedAtUtc = now
        };
        var (token, hash) = InviteToken.Generate();
        var invite = new StudentInvite
        {
            Id = Guid.NewGuid(),
            PersonalStudentId = link.Id,
            TokenHash = hash,
            ExpiresAtUtc = now + InviteToken.Validity,
            CreatedAtUtc = now
        };

        db.PersonalStudents.Add(link);
        db.StudentInvites.Add(invite);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = command.PersonalUserId,
            Action = "StudentInvited",
            EntityType = nameof(PersonalStudent),
            EntityId = link.Id,
            OccurredAtUtc = now,
            CorrelationId = command.CorrelationId,
            // Sem nome, e-mail ou celular: só referências.
            Metadata = JsonSerializer.Serialize(new { inviteId = invite.Id, expiresAtUtc = invite.ExpiresAtUtc }),
            CreatedAtUtc = now
        });

        // SaveChanges grava as três linhas numa única transação implícita.
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           {
                                               SqlState: PostgresErrorCodes.UniqueViolation,
                                               ConstraintName: StudentFields.DuplicateEmailIndex
                                           })
        {
            // Dois envios simultâneos com o mesmo e-mail: o índice único decide.
            return CreateStudentResult.Failed(CreateStudentOutcome.DuplicateEmail);
        }

        LogInvited(logger, link.Id, command.PersonalUserId);
        return new CreateStudentResult(CreateStudentOutcome.Succeeded, new Dictionary<string, string>(), link.Id, token);
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information,
        Message = "Aluno {PersonalStudentId} cadastrado e convidado pelo personal {UserId}")]
    private static partial void LogInvited(ILogger logger, Guid personalStudentId, Guid userId);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "Cadastro de aluno recusado: usuário {UserId} sem perfil de personal ativo")]
    private static partial void LogForbidden(ILogger logger, Guid userId);
}
