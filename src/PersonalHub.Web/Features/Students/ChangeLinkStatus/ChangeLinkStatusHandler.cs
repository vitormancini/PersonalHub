using Microsoft.EntityFrameworkCore;
using Npgsql;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Students.ChangeLinkStatus;

public enum ChangeLinkStatusOutcome
{
    Succeeded,

    /// <summary>Reativado sem conta do aluno: voltou para <c>Invited</c> com um convite novo (token no resultado).</summary>
    ReactivatedWithInvite,

    /// <summary>Vínculo inexistente, de outro personal, ou personal inativo.</summary>
    NotFound,

    /// <summary>O vínculo já está na situação pedida (duplo toque, outra aba).</summary>
    AlreadyDone,

    /// <summary>Reativação barrada: outro vínculo não inativo do personal usa o mesmo e-mail.</summary>
    EmailInUse
}

public sealed record ChangeLinkStatusResult(ChangeLinkStatusOutcome Outcome, string? InviteToken = null);

/// <summary>Inativação e reativação do vínculo pelo personal (D-028, D-037).</summary>
public sealed partial class ChangeLinkStatusHandler(AppDbContext db, TimeProvider clock, ILogger<ChangeLinkStatusHandler> logger)
{
    /// <summary>Mantém o histórico, revoga o convite em aberto e preenche <c>EndedAtUtc</c>.</summary>
    public async Task<ChangeLinkStatusResult> InactivateAsync(
        Guid personalUserId, Guid personalStudentId, string? correlationId, CancellationToken cancellationToken)
    {
        var personalId = await db.ActivePersonalIdAsync(personalUserId, cancellationToken);
        if (personalId is null)
        {
            return new ChangeLinkStatusResult(ChangeLinkStatusOutcome.NotFound);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var link = await db.LockOwnedAsync(personalId.Value, personalStudentId, cancellationToken);
        if (link is null)
        {
            return new ChangeLinkStatusResult(ChangeLinkStatusOutcome.NotFound);
        }

        if (link.Status == PersonalStudentStatus.Inactive)
        {
            return new ChangeLinkStatusResult(ChangeLinkStatusOutcome.AlreadyDone);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var previous = link.Status;
        var revoked = await db.RevokeOpenInvitesAsync(link.Id, now, cancellationToken);
        link.Inactivate(now);
        db.AddAudit(personalUserId, "StudentLinkInactivated", link.Id, now, correlationId,
            new { previousStatus = previous.ToString(), revokedInvites = revoked });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogInactivated(logger, link.Id, personalUserId);
        return new ChangeLinkStatusResult(ChangeLinkStatusOutcome.Succeeded);
    }

    /// <summary>
    /// Volta para <c>Active</c> se o aluno tem conta; senão, para <c>Invited</c> com um convite novo. Bloqueado se o
    /// personal já tem outro vínculo não inativo com o mesmo e-mail (o índice único também garante).
    /// </summary>
    public async Task<ChangeLinkStatusResult> ReactivateAsync(
        Guid personalUserId, Guid personalStudentId, string? correlationId, CancellationToken cancellationToken)
    {
        var personalId = await db.ActivePersonalIdAsync(personalUserId, cancellationToken);
        if (personalId is null)
        {
            return new ChangeLinkStatusResult(ChangeLinkStatusOutcome.NotFound);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var link = await db.LockOwnedAsync(personalId.Value, personalStudentId, cancellationToken);
        if (link is null)
        {
            return new ChangeLinkStatusResult(ChangeLinkStatusOutcome.NotFound);
        }

        if (link.Status != PersonalStudentStatus.Inactive)
        {
            return new ChangeLinkStatusResult(ChangeLinkStatusOutcome.AlreadyDone);
        }

        if (await db.EmailInUseByAnotherLinkAsync(link, cancellationToken))
        {
            return new ChangeLinkStatusResult(ChangeLinkStatusOutcome.EmailInUse);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        link.Reactivate();
        string? token = null;
        Guid? inviteId = null;
        if (link.Status == PersonalStudentStatus.Invited)
        {
            var (invite, newToken) = db.AddInvite(link.Id, now);
            token = newToken;
            inviteId = invite.Id;
        }

        db.AddAudit(personalUserId, "StudentLinkReactivated", link.Id, now, correlationId,
            new { status = link.Status.ToString(), inviteId });

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
            // Outro vínculo com o mesmo e-mail foi criado ou reativado em paralelo.
            return new ChangeLinkStatusResult(ChangeLinkStatusOutcome.EmailInUse);
        }

        await transaction.CommitAsync(cancellationToken);

        LogReactivated(logger, link.Id, personalUserId, link.Status);
        return token is null
            ? new ChangeLinkStatusResult(ChangeLinkStatusOutcome.Succeeded)
            : new ChangeLinkStatusResult(ChangeLinkStatusOutcome.ReactivatedWithInvite, token);
    }

    [LoggerMessage(EventId = 3220, Level = LogLevel.Information,
        Message = "Vínculo {PersonalStudentId} inativado pelo personal {UserId}")]
    private static partial void LogInactivated(ILogger logger, Guid personalStudentId, Guid userId);

    [LoggerMessage(EventId = 3221, Level = LogLevel.Information,
        Message = "Vínculo {PersonalStudentId} reativado pelo personal {UserId}; situação {Status}")]
    private static partial void LogReactivated(ILogger logger, Guid personalStudentId, Guid userId, PersonalStudentStatus status);
}
