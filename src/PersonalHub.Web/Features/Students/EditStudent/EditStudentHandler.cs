using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Students.EditStudent;

public sealed record EditStudentCommand(Guid PersonalUserId, Guid PersonalStudentId, EditStudentInput Input, string? CorrelationId);

public enum EditStudentOutcome
{
    Succeeded,
    Invalid,

    /// <summary>O novo e-mail já está em outro vínculo não inativo do personal (mesma regra do cadastro).</summary>
    DuplicateEmail,

    /// <summary>O vínculo mudou depois que o formulário foi aberto (xmin diferente).</summary>
    Conflict,

    /// <summary>Vínculo inexistente, de outro personal, ou personal inativo.</summary>
    NotFound
}

public sealed record EditStudentResult(
    EditStudentOutcome Outcome,
    IReadOnlyDictionary<string, string> Errors,
    string? NewInviteToken = null)
{
    public static EditStudentResult Failed(EditStudentOutcome outcome, IReadOnlyDictionary<string, string>? errors = null) =>
        new(outcome, errors ?? new Dictionary<string, string>());
}

/// <summary>
/// Edição dos dados do vínculo (D-035, D-036). O e-mail só muda enquanto o convite não foi aceito; trocar o e-mail
/// revoga o convite em aberto e gera outro na mesma transação. Valor e vencimento novos não mexem em nada já
/// registrado: o preço é congelado em cada aula (D-018) e o vencimento só vale no próximo demonstrativo.
/// </summary>
public sealed partial class EditStudentHandler(AppDbContext db, TimeProvider clock, ILogger<EditStudentHandler> logger)
{
    public async Task<EditStudentResult> HandleAsync(EditStudentCommand command, CancellationToken cancellationToken)
    {
        var personalId = await db.ActivePersonalIdAsync(command.PersonalUserId, cancellationToken);
        if (personalId is null)
        {
            return EditStudentResult.Failed(EditStudentOutcome.NotFound);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var link = await db.LockOwnedAsync(personalId.Value, command.PersonalStudentId, cancellationToken);
        if (link is null)
        {
            return EditStudentResult.Failed(EditStudentOutcome.NotFound);
        }

        var input = command.Input;
        var version = db.Entry(link).Property<uint>("xmin").CurrentValue;
        if (!uint.TryParse(input.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var posted) || posted != version)
        {
            LogConflict(logger, link.Id, command.PersonalUserId);
            return EditStudentResult.Failed(EditStudentOutcome.Conflict);
        }

        // Depois do aceite o e-mail é o login do aluno: o valor enviado é ignorado, mesmo que o formulário seja forjado.
        var email = link.CanEditEmail ? input.Email : link.StudentEmail;
        var errors = StudentFields.Validate(input.Name, email, input.Phone, input.LessonPrice, input.BillingDueDay, out var data);
        if (errors.Count > 0)
        {
            return EditStudentResult.Failed(EditStudentOutcome.Invalid, errors);
        }

        var emailChanged = data.Email != link.StudentEmail;
        if (emailChanged && await db.PersonalStudents.AnyAsync(
                s => s.PersonalId == link.PersonalId && s.Id != link.Id && s.StudentEmail == data.Email
                     && s.Status != PersonalStudentStatus.Inactive,
                cancellationToken))
        {
            return EditStudentResult.Failed(EditStudentOutcome.DuplicateEmail);
        }

        var changed = new List<string>();
        if (data.Name != link.StudentName) changed.Add(nameof(PersonalStudent.StudentName));
        if (emailChanged) changed.Add(nameof(PersonalStudent.StudentEmail));
        if (data.Phone != link.StudentPhone) changed.Add(nameof(PersonalStudent.StudentPhone));
        if (data.LessonPrice != link.LessonPrice) changed.Add(nameof(PersonalStudent.LessonPrice));
        if (data.BillingDueDay != link.BillingDueDay) changed.Add(nameof(PersonalStudent.BillingDueDay));
        if (changed.Count == 0)
        {
            return new EditStudentResult(EditStudentOutcome.Succeeded, new Dictionary<string, string>());
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var previousPrice = link.LessonPrice;
        var previousDueDay = link.BillingDueDay;
        link.StudentName = data.Name;
        link.StudentEmail = data.Email;
        link.StudentPhone = data.Phone;
        link.LessonPrice = data.LessonPrice;
        link.BillingDueDay = data.BillingDueDay;

        string? token = null;
        Guid? newInviteId = null;
        var revoked = 0;
        if (emailChanged)
        {
            // O link antigo foi enviado para outra pessoa (ou endereço errado): deixa de valer na hora.
            revoked = await db.RevokeOpenInvitesAsync(link.Id, now, cancellationToken);
            var (invite, newToken) = db.AddInvite(link.Id, now);
            token = newToken;
            newInviteId = invite.Id;
        }

        // Só nomes de campos e valores de cobrança; nada de nome, e-mail ou celular.
        db.AddAudit(command.PersonalUserId, "StudentUpdated", link.Id, now, command.CorrelationId, new
        {
            changed,
            lessonPrice = data.LessonPrice != previousPrice ? new { from = previousPrice, to = data.LessonPrice } : null,
            billingDueDay = data.BillingDueDay != previousDueDay ? new { from = previousDueDay, to = data.BillingDueDay } : null,
            newInviteId,
            revokedInvites = revoked
        });

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
            return EditStudentResult.Failed(EditStudentOutcome.DuplicateEmail);
        }

        await transaction.CommitAsync(cancellationToken);

        LogUpdated(logger, link.Id, command.PersonalUserId, emailChanged);
        return new EditStudentResult(EditStudentOutcome.Succeeded, new Dictionary<string, string>(), token);
    }

    [LoggerMessage(EventId = 3210, Level = LogLevel.Information,
        Message = "Vínculo {PersonalStudentId} editado pelo personal {UserId} (e-mail trocado: {EmailChanged})")]
    private static partial void LogUpdated(ILogger logger, Guid personalStudentId, Guid userId, bool emailChanged);

    [LoggerMessage(EventId = 3211, Level = LogLevel.Warning,
        Message = "Edição do vínculo {PersonalStudentId} pelo personal {UserId} recusada: versão desatualizada")]
    private static partial void LogConflict(ILogger logger, Guid personalStudentId, Guid userId);
}
