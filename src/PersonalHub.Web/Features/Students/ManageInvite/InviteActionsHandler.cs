using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Students.ManageInvite;

public enum InviteActionOutcome
{
    Succeeded,

    /// <summary>Vínculo inexistente, de outro personal, ou personal inativo: a página responde 404.</summary>
    NotFound,

    /// <summary>O vínculo não está mais aguardando aceite (o aluno aceitou ou o vínculo foi inativado).</summary>
    NotInvited
}

public sealed record InviteActionResult(InviteActionOutcome Outcome, string? InviteToken = null);

/// <summary>
/// Reenvio e cancelamento do convite pelo personal (D-034). Reenviar gera um novo link e revoga na hora os anteriores
/// (só o hash é guardado, então o link antigo não pode ser mostrado de novo). Cancelar só revoga.
/// </summary>
public sealed partial class InviteActionsHandler(AppDbContext db, TimeProvider clock, ILogger<InviteActionsHandler> logger)
{
    public async Task<InviteActionResult> RenewAsync(
        Guid personalUserId, Guid personalStudentId, string? correlationId, CancellationToken cancellationToken)
    {
        var personalId = await db.ActivePersonalIdAsync(personalUserId, cancellationToken);
        if (personalId is null)
        {
            return new InviteActionResult(InviteActionOutcome.NotFound);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var link = await db.LockOwnedAsync(personalId.Value, personalStudentId, cancellationToken);
        if (link is null)
        {
            return new InviteActionResult(InviteActionOutcome.NotFound);
        }

        if (link.Status != PersonalStudentStatus.Invited)
        {
            return new InviteActionResult(InviteActionOutcome.NotInvited);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var revoked = await db.RevokeOpenInvitesAsync(link.Id, now, cancellationToken);
        var (invite, token) = db.AddInvite(link.Id, now);
        db.AddAudit(personalUserId, "StudentInviteRenewed", link.Id, now, correlationId,
            new { inviteId = invite.Id, expiresAtUtc = invite.ExpiresAtUtc, revokedInvites = revoked });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogRenewed(logger, link.Id, personalUserId, revoked);
        return new InviteActionResult(InviteActionOutcome.Succeeded, token);
    }

    /// <summary>Revoga o convite em aberto. Repetir o pedido (duplo toque) não muda nada nem audita de novo.</summary>
    public async Task<InviteActionResult> CancelAsync(
        Guid personalUserId, Guid personalStudentId, string? correlationId, CancellationToken cancellationToken)
    {
        var personalId = await db.ActivePersonalIdAsync(personalUserId, cancellationToken);
        if (personalId is null)
        {
            return new InviteActionResult(InviteActionOutcome.NotFound);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var link = await db.LockOwnedAsync(personalId.Value, personalStudentId, cancellationToken);
        if (link is null)
        {
            return new InviteActionResult(InviteActionOutcome.NotFound);
        }

        if (link.Status != PersonalStudentStatus.Invited)
        {
            return new InviteActionResult(InviteActionOutcome.NotInvited);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var revoked = await db.RevokeOpenInvitesAsync(link.Id, now, cancellationToken);
        if (revoked > 0)
        {
            db.AddAudit(personalUserId, "StudentInviteCancelled", link.Id, now, correlationId,
                new { revokedInvites = revoked });
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        LogCancelled(logger, link.Id, personalUserId, revoked);
        return new InviteActionResult(InviteActionOutcome.Succeeded);
    }

    [LoggerMessage(EventId = 3200, Level = LogLevel.Information,
        Message = "Novo convite gerado para o vínculo {PersonalStudentId} pelo personal {UserId}; {Revoked} convite(s) revogado(s)")]
    private static partial void LogRenewed(ILogger logger, Guid personalStudentId, Guid userId, int revoked);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Information,
        Message = "Convite do vínculo {PersonalStudentId} cancelado pelo personal {UserId}; {Revoked} convite(s) revogado(s)")]
    private static partial void LogCancelled(ILogger logger, Guid personalStudentId, Guid userId, int revoked);
}
