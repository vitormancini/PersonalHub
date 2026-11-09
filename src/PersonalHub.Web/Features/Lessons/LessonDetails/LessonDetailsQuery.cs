using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Lessons.LessonDetails;

public sealed record LessonDetailsView(
    Guid Id,
    Guid PersonalStudentId,
    string StudentName,
    string PersonalName,
    DateTime OccurredAtUtc,
    short? DurationMinutes,
    string? Notes,
    decimal UnitPrice,
    LessonStatus Status,
    DateTime? RespondedAtUtc,
    string? RejectionReason,
    DateTime CreatedAtUtc,
    DateTime? DeletedAtUtc,
    string? DeletionReason)
{
    public bool Deleted => DeletedAtUtc is not null;
    public bool CanRespond => Status == LessonStatus.Pending && !Deleted;
}

/// <summary>
/// Uma aula vista pelo personal dono do vínculo ou pelo aluno do vínculo. Aula de outro usuário devolve nulo, como se
/// não existisse.
/// </summary>
public sealed class LessonDetailsQuery(AppDbContext db)
{
    public Task<LessonDetailsView?> ForPersonalAsync(Guid personalUserId, Guid lessonId, CancellationToken cancellationToken) =>
        Project(db.Lessons.Where(l => l.Id == lessonId),
            db.PersonalProfiles.Where(p => p.UserId == personalUserId && p.IsActive),
            db.PersonalStudents, cancellationToken);

    /// <summary>
    /// Aula excluída ainda pendente some da visão do aluno (D-042): ele nunca respondeu, então não há o que mostrar.
    /// </summary>
    public Task<LessonDetailsView?> ForStudentAsync(Guid studentUserId, Guid lessonId, CancellationToken cancellationToken) =>
        Project(db.Lessons.Where(l => l.Id == lessonId && !(l.DeletedAtUtc != null && l.Status == LessonStatus.Pending)),
            db.PersonalProfiles,
            db.PersonalStudents.Where(s => s.StudentUserId == studentUserId), cancellationToken);

    private static Task<LessonDetailsView?> Project(
        IQueryable<Lesson> lessons, IQueryable<PersonalProfile> personals, IQueryable<PersonalStudent> links,
        CancellationToken cancellationToken) =>
        lessons
            .Join(links, l => l.PersonalStudentId, s => s.Id, (l, s) => new { l, s })
            .Join(personals, x => x.s.PersonalId, p => p.Id, (x, p) => new LessonDetailsView(
                x.l.Id, x.l.PersonalStudentId, x.s.StudentName, p.DisplayName, x.l.OccurredAtUtc, x.l.DurationMinutes,
                x.l.Notes, x.l.UnitPrice, x.l.Status, x.l.RespondedAtUtc, x.l.RejectionReason, x.l.CreatedAtUtc,
                x.l.DeletedAtUtc, x.l.DeletionReason))
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
}
