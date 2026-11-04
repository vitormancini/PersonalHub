using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Students.CreateStudent;

public sealed record InviteCreatedView(
    string StudentName,
    string StudentPhone,
    string PersonalName,
    PersonalStudentStatus Status,
    DateTime? InviteExpiresAtUtc);

/// <summary>Dados da página "Convite do aluno". Só devolve o vínculo se ele pertencer ao personal logado.</summary>
public sealed class InviteCreatedQuery(AppDbContext db)
{
    public Task<InviteCreatedView?> GetAsync(Guid personalUserId, Guid personalStudentId, CancellationToken cancellationToken) =>
        db.PersonalStudents
            .Where(s => s.Id == personalStudentId)
            .Join(db.PersonalProfiles.Where(p => p.UserId == personalUserId),
                s => s.PersonalId, p => p.Id, (s, p) => new { s, p })
            .Select(x => new InviteCreatedView(
                x.s.StudentName,
                x.s.StudentPhone,
                x.p.DisplayName,
                x.s.Status,
                db.StudentInvites
                    .Where(i => i.PersonalStudentId == x.s.Id)
                    .OrderByDescending(i => i.CreatedAtUtc)
                    .Select(i => (DateTime?)i.ExpiresAtUtc)
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);
}
