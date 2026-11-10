using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Lessons.ListLessons;

public sealed record LessonListItem(
    Guid Id,
    DateTime OccurredAtUtc,
    string StudentName,
    short? DurationMinutes,
    decimal UnitPrice,
    LessonStatus Status,
    bool Deleted);

/// <summary>Contagem do mês por situação, sem as excluídas (que têm contagem própria).</summary>
public sealed record LessonCounts(int Pending, int Approved, int Rejected, int Deleted)
{
    public int Of(LessonStatus status) => status switch
    {
        LessonStatus.Approved => Approved,
        LessonStatus.Rejected => Rejected,
        _ => Pending
    };
}

public sealed record LessonFilterStudent(Guid Id, string Name, bool Inactive);

public sealed record LessonListView(
    IReadOnlyList<LessonListItem> Items,
    LessonCounts Counts,
    IReadOnlyList<LessonFilterStudent> Students);

/// <summary>Aulas do personal logado num mês de São Paulo, com filtros por situação e aluno.</summary>
public sealed class ListLessonsQuery(AppDbContext db)
{
    private static readonly StringComparer NameOrder = StringComparer.Create(StudentDisplay.PtBr, CompareOptions.IgnoreCase);

    public async Task<LessonListView> GetAsync(
        Guid personalUserId, DateOnly month, LessonStatus? status, Guid? studentId, bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var students = await OwnedLinks(personalUserId)
            .Select(s => new LessonFilterStudent(s.Id, s.StudentName, s.Status == PersonalStudentStatus.Inactive))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var counts = await CountsAsync(personalUserId, month, studentId, cancellationToken);

        var lessons = LessonsInMonth(personalUserId, month, studentId);
        if (status is not null)
        {
            lessons = lessons.Where(x => x.Lesson.Status == status);
        }

        if (!includeDeleted)
        {
            lessons = lessons.Where(x => x.Lesson.DeletedAtUtc == null);
        }

        var items = await lessons
            .OrderByDescending(x => x.Lesson.OccurredAtUtc)
            .ThenByDescending(x => x.Lesson.CreatedAtUtc)
            .Select(x => new LessonListItem(x.Lesson.Id, x.Lesson.OccurredAtUtc, x.StudentName, x.Lesson.DurationMinutes,
                x.Lesson.UnitPrice, x.Lesson.Status, x.Lesson.DeletedAtUtc != null))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new LessonListView(items, counts, students.OrderBy(s => s.Name, NameOrder).ToList());
    }

    /// <summary>Contagens do mês (também usadas no resumo do detalhe do aluno).</summary>
    public async Task<LessonCounts> CountsAsync(
        Guid personalUserId, DateOnly month, Guid? studentId, CancellationToken cancellationToken)
    {
        var groups = await LessonsInMonth(personalUserId, month, studentId)
            .GroupBy(x => new { x.Lesson.Status, Deleted = x.Lesson.DeletedAtUtc != null })
            .Select(g => new { g.Key.Status, g.Key.Deleted, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Count(LessonStatus status) => groups.Where(g => !g.Deleted && g.Status == status).Sum(g => g.Count);
        return new LessonCounts(Count(LessonStatus.Pending), Count(LessonStatus.Approved), Count(LessonStatus.Rejected),
            groups.Where(g => g.Deleted).Sum(g => g.Count));
    }

    private IQueryable<PersonalStudent> OwnedLinks(Guid personalUserId) =>
        db.PersonalStudents
            .Join(db.PersonalProfiles.Where(p => p.UserId == personalUserId && p.IsActive),
                s => s.PersonalId, p => p.Id, (s, p) => s);

    private IQueryable<LessonRow> LessonsInMonth(Guid personalUserId, DateOnly month, Guid? studentId)
    {
        var (startUtc, endUtc) = SaoPaulo.MonthRangeUtc(month.Year, month.Month);
        var links = OwnedLinks(personalUserId);
        if (studentId is not null)
        {
            links = links.Where(s => s.Id == studentId);
        }

        return db.Lessons
            .Where(l => l.OccurredAtUtc >= startUtc && l.OccurredAtUtc < endUtc)
            .Join(links, l => l.PersonalStudentId, s => s.Id, (l, s) => new LessonRow { Lesson = l, StudentName = s.StudentName });
    }

    private sealed class LessonRow
    {
        public required Lesson Lesson { get; init; }
        public required string StudentName { get; init; }
    }
}
