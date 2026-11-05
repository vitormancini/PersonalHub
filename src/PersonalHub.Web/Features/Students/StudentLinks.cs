using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Students;

/// <summary>Operações comuns aos handlers que alteram um vínculo do personal logado.</summary>
public static class StudentLinks
{
    /// <summary>Id do <see cref="PersonalProfile"/> ativo do usuário, ou nulo.</summary>
    public static Task<Guid?> ActivePersonalIdAsync(this AppDbContext db, Guid personalUserId, CancellationToken cancellationToken) =>
        db.PersonalProfiles
            .Where(p => p.UserId == personalUserId && p.IsActive)
            .Select(p => (Guid?)p.Id)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Carrega o vínculo do personal com <c>SELECT ... FOR UPDATE</c>; precisa de transação aberta. Duas ações sobre o
    /// mesmo vínculo (ou uma ação e o aceite do convite) passam a acontecer uma depois da outra, e a segunda já lê o
    /// estado gravado pela primeira. Vínculo de outro personal ou inexistente devolve nulo.
    /// </summary>
    public static async Task<PersonalStudent?> LockOwnedAsync(
        this AppDbContext db, Guid personalId, Guid personalStudentId, CancellationToken cancellationToken)
    {
        // xmin é coluna de sistema (fora do *) e o EF precisa dela como token de concorrência. Sem composição sobre o
        // FromSql: o EF envia o SQL como está e o FOR UPDATE vale.
        var rows = await db.PersonalStudents
            .FromSqlInterpolated($"""
                SELECT *, xmin FROM "PersonalStudent"
                WHERE "Id" = {personalStudentId} AND "PersonalId" = {personalId}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    /// <summary>Outro vínculo não inativo do mesmo personal usa o e-mail deste (impede a reativação, D-037).</summary>
    public static Task<bool> EmailInUseByAnotherLinkAsync(this AppDbContext db, PersonalStudent link, CancellationToken cancellationToken) =>
        db.PersonalStudents.AnyAsync(
            s => s.PersonalId == link.PersonalId && s.Id != link.Id && s.StudentEmail == link.StudentEmail
                 && s.Status != PersonalStudentStatus.Inactive,
            cancellationToken);

    /// <summary>Revoga os convites ainda em aberto do vínculo e devolve quantos foram revogados.</summary>
    public static Task<int> RevokeOpenInvitesAsync(
        this AppDbContext db, Guid personalStudentId, DateTime nowUtc, CancellationToken cancellationToken) =>
        db.StudentInvites
            .Where(i => i.PersonalStudentId == personalStudentId && i.UsedAtUtc == null && i.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAtUtc, nowUtc), cancellationToken);

    /// <summary>Novo convite de 7 dias (D-016). Devolve o token puro, que nunca é gravado.</summary>
    public static (StudentInvite Invite, string Token) AddInvite(this AppDbContext db, Guid personalStudentId, DateTime nowUtc)
    {
        var (token, hash) = InviteToken.Generate();
        var invite = new StudentInvite
        {
            Id = Guid.NewGuid(),
            PersonalStudentId = personalStudentId,
            TokenHash = hash,
            ExpiresAtUtc = nowUtc + InviteToken.Validity,
            CreatedAtUtc = nowUtc
        };
        db.StudentInvites.Add(invite);
        return (invite, token);
    }

    /// <summary>Auditoria de uma ação do personal sobre o vínculo. O metadata não leva nome, e-mail, celular nem token.</summary>
    public static void AddAudit(
        this AppDbContext db, Guid actorUserId, string action, Guid personalStudentId, DateTime nowUtc,
        string? correlationId, object? metadata = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = nameof(PersonalStudent),
            EntityId = personalStudentId,
            OccurredAtUtc = nowUtc,
            CorrelationId = correlationId,
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata),
            CreatedAtUtc = nowUtc
        });
}
