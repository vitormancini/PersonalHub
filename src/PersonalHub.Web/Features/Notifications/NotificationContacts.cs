using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Notifications;

/// <summary>
/// Quem recebe os avisos de um vínculo. O aluno recebe no celular confirmado da conta (sem conta ou sem celular
/// confirmado: nulo, e o aviso é encerrado como <c>sem-celular</c>). O personal recebe no celular do perfil.
/// O personal vê o aluno pelo nome que cadastrou (D-015); o aluno vê o personal pelo nome de exibição.
/// </summary>
public sealed record LinkContacts(
    Guid PersonalStudentId,
    Guid? StudentUserId,
    string? StudentPhone,
    string StudentFirstName,
    string StudentName,
    Guid PersonalUserId,
    string? PersonalPhone,
    string PersonalName);

public static class NotificationContacts
{
    public static async Task<LinkContacts?> LinkContactsAsync(
        this AppDbContext db, Guid personalStudentId, CancellationToken cancellationToken)
    {
        var row = await (
                from s in db.PersonalStudents
                join p in db.PersonalProfiles on s.PersonalId equals p.Id
                join pu in db.Users on p.UserId equals pu.Id
                join su in db.Users on s.StudentUserId equals su.Id into students
                from su in students.DefaultIfEmpty()
                where s.Id == personalStudentId
                select new
                {
                    s.StudentUserId,
                    s.StudentName,
                    StudentFullName = su == null ? null : su.FullName,
                    StudentPhone = su == null ? null : su.PhoneNumber,
                    StudentPhoneConfirmed = su != null && su.PhoneNumberConfirmed,
                    PersonalUserId = p.UserId,
                    p.DisplayName,
                    ProfilePhone = p.PhoneNumber,
                    PersonalAccountPhone = pu.PhoneNumber,
                    PersonalAccountPhoneConfirmed = pu.PhoneNumberConfirmed
                })
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var studentPhone = row.StudentPhoneConfirmed ? BrazilianPhone.ToE164(row.StudentPhone) : null;
        var personalPhone = BrazilianPhone.ToE164(row.ProfilePhone)
                            ?? (row.PersonalAccountPhoneConfirmed ? BrazilianPhone.ToE164(row.PersonalAccountPhone) : null);

        return new LinkContacts(
            personalStudentId,
            row.StudentUserId,
            row.StudentUserId is null ? null : studentPhone,
            Domain.Notifications.NotificationTemplates.FirstName(row.StudentFullName ?? row.StudentName),
            row.StudentName,
            row.PersonalUserId,
            personalPhone,
            row.DisplayName);
    }
}
