using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Students.StudentDetails;

public sealed record StudentDetailsView(
    Guid Id,
    string StudentName,
    string StudentEmail,
    string StudentPhone,
    decimal LessonPrice,
    short BillingDueDay,
    PersonalStudentStatus Status,
    bool HasAccount,
    DateTime? EndedAtUtc,
    InviteStatus? Invite,
    DateTime? InviteCreatedAtUtc,
    DateTime? InviteExpiresAtUtc,
    DateTime? InviteRevokedAtUtc,
    bool EmailInUse);

/// <summary>
/// Vínculo do personal logado com o convite mais recente. Usado pelos detalhes e pela confirmação de
/// inativar/reativar. Vínculo de outro personal (ou personal inativo) devolve nulo, como se não existisse.
/// </summary>
public sealed class StudentDetailsQuery(AppDbContext db, TimeProvider clock)
{
    public async Task<StudentDetailsView?> GetAsync(Guid personalUserId, Guid personalStudentId, CancellationToken cancellationToken)
    {
        var row = await db.PersonalStudents
            .Where(s => s.Id == personalStudentId)
            .Join(db.PersonalProfiles.Where(p => p.UserId == personalUserId && p.IsActive),
                s => s.PersonalId, p => p.Id, (s, p) => s)
            .Select(s => new
            {
                Link = s,
                Invite = db.StudentInvites
                    .Where(i => i.PersonalStudentId == s.Id)
                    .OrderByDescending(i => i.CreatedAtUtc)
                    .Select(i => new { i.CreatedAtUtc, i.ExpiresAtUtc, i.UsedAtUtc, i.RevokedAtUtc })
                    .FirstOrDefault(),
                EmailInUse = s.Status == PersonalStudentStatus.Inactive && db.PersonalStudents.Any(o =>
                    o.PersonalId == s.PersonalId && o.Id != s.Id && o.StudentEmail == s.StudentEmail
                    && o.Status != PersonalStudentStatus.Inactive)
            })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var link = row.Link;
        InviteStatus? invite = link.Status == PersonalStudentStatus.Invited
            ? InviteStatusRules.Of(row.Invite?.ExpiresAtUtc, row.Invite?.UsedAtUtc, row.Invite?.RevokedAtUtc,
                clock.GetUtcNow().UtcDateTime)
            : null;
        return new StudentDetailsView(link.Id, link.StudentName, link.StudentEmail, link.StudentPhone, link.LessonPrice,
            link.BillingDueDay, link.Status, link.StudentUserId is not null, link.EndedAtUtc, invite,
            row.Invite?.CreatedAtUtc, row.Invite?.ExpiresAtUtc, row.Invite?.RevokedAtUtc, row.EmailInUse);
    }
}
