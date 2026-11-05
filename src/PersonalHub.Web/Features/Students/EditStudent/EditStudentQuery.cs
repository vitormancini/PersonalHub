using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Students.EditStudent;

public sealed record EditStudentView(string StudentName, PersonalStudentStatus Status, bool CanEditEmail, EditStudentInput Input);

/// <summary>Dados atuais do vínculo para preencher o formulário de edição. Só do personal logado e ativo.</summary>
public sealed class EditStudentQuery(AppDbContext db)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<EditStudentView?> GetAsync(Guid personalUserId, Guid personalStudentId, CancellationToken cancellationToken)
    {
        var row = await db.PersonalStudents
            .Where(s => s.Id == personalStudentId)
            .Join(db.PersonalProfiles.Where(p => p.UserId == personalUserId && p.IsActive),
                s => s.PersonalId, p => p.Id, (s, p) => s)
            .Select(s => new
            {
                s.StudentName,
                s.StudentEmail,
                s.StudentPhone,
                s.LessonPrice,
                s.BillingDueDay,
                s.Status,
                Version = EF.Property<uint>(s, "xmin")
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        return new EditStudentView(row.StudentName, row.Status, row.Status == PersonalStudentStatus.Invited, new EditStudentInput
        {
            Name = row.StudentName,
            Email = row.StudentEmail,
            Phone = BrazilianPhone.ToDisplay(row.StudentPhone),
            LessonPrice = row.LessonPrice.ToString("N2", PtBr),
            BillingDueDay = row.BillingDueDay.ToString(CultureInfo.InvariantCulture),
            Version = row.Version.ToString(CultureInfo.InvariantCulture)
        });
    }
}
