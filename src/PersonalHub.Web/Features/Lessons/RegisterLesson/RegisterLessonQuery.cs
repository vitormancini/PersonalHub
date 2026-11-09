using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Lessons.RegisterLesson;

public sealed record LessonStudentOption(Guid Id, string Name, decimal LessonPrice, bool Invited);

/// <summary>Alunos que podem receber aula (D-039): vínculos <c>Active</c> e <c>Invited</c>, em ordem alfabética.</summary>
public sealed class RegisterLessonQuery(AppDbContext db)
{
    private static readonly StringComparer NameOrder = StringComparer.Create(StudentDisplay.PtBr, CompareOptions.IgnoreCase);

    public async Task<IReadOnlyList<LessonStudentOption>> StudentsAsync(Guid personalUserId, CancellationToken cancellationToken)
    {
        var rows = await db.PersonalStudents
            .Join(db.PersonalProfiles.Where(p => p.UserId == personalUserId && p.IsActive),
                s => s.PersonalId, p => p.Id, (s, p) => s)
            .Where(s => s.Status != PersonalStudentStatus.Inactive)
            .Select(s => new LessonStudentOption(s.Id, s.StudentName, s.LessonPrice, s.Status == PersonalStudentStatus.Invited))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.OrderBy(r => r.Name, NameOrder).ToList();
    }
}
