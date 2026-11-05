using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Features.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Lessons.StudentLessons;

public sealed record StudentLessonItem(
    Guid Id,
    DateTime OccurredAtUtc,
    string PersonalName,
    short? DurationMinutes,
    decimal UnitPrice,
    LessonStatus Status,
    bool Deleted);

public sealed record StudentLessonsView(
    IReadOnlyList<string> PersonalNames,
    IReadOnlyList<StudentLessonItem> Pending,
    IReadOnlyList<StudentLessonItem> History);

/// <summary>
/// Aulas do aluno logado em todos os vínculos dele (um aluno pode ter vários personais, D-015). Aula pendente excluída
/// não aparece; respondida e excluída aparece como excluída (D-042).
/// </summary>
public sealed class StudentLessonsQuery(AppDbContext db)
{
    /// <summary>O histórico mostra as respostas mais recentes; a lista de um aluno cresce pouco por mês.</summary>
    public const int HistoryLimit = 200;

    private static readonly StringComparer NameOrder = StringComparer.Create(StudentDisplay.PtBr, CompareOptions.IgnoreCase);

    public async Task<StudentLessonsView> GetAsync(Guid studentUserId, CancellationToken cancellationToken)
    {
        var names = await Links(studentUserId)
            .Select(x => x.PersonalName)
            .Distinct()
            .ToListAsync(cancellationToken);

        var pending = await Items(Lessons(studentUserId, l => l.Status == LessonStatus.Pending && l.DeletedAtUtc == null))
            .ToListAsync(cancellationToken);

        var history = await Items(Lessons(studentUserId, l => l.Status != LessonStatus.Pending))
            .Take(HistoryLimit)
            .ToListAsync(cancellationToken);

        return new StudentLessonsView(names.Order(NameOrder).ToList(), pending, history);
    }

    public Task<int> PendingCountAsync(Guid studentUserId, CancellationToken cancellationToken) =>
        Lessons(studentUserId, l => l.Status == LessonStatus.Pending && l.DeletedAtUtc == null).CountAsync(cancellationToken);

    private IQueryable<LinkRow> Links(Guid studentUserId) =>
        db.PersonalStudents
            .Where(s => s.StudentUserId == studentUserId)
            .Join(db.PersonalProfiles, s => s.PersonalId, p => p.Id,
                (s, p) => new LinkRow { Id = s.Id, PersonalName = p.DisplayName });

    private IQueryable<LessonRow> Lessons(Guid studentUserId, Expression<Func<Lesson, bool>> filter) =>
        db.Lessons
            .Where(filter)
            .Join(Links(studentUserId), l => l.PersonalStudentId, s => s.Id,
                (l, s) => new LessonRow { Lesson = l, PersonalName = s.PersonalName });

    private static IQueryable<StudentLessonItem> Items(IQueryable<LessonRow> rows) =>
        rows
            .OrderByDescending(x => x.Lesson.OccurredAtUtc)
            .Select(x => new StudentLessonItem(x.Lesson.Id, x.Lesson.OccurredAtUtc, x.PersonalName,
                x.Lesson.DurationMinutes, x.Lesson.UnitPrice, x.Lesson.Status, x.Lesson.DeletedAtUtc != null))
            .AsNoTracking();

    private sealed class LessonRow
    {
        public required Lesson Lesson { get; init; }
        public required string PersonalName { get; init; }
    }

    private sealed class LinkRow
    {
        public Guid Id { get; init; }
        public required string PersonalName { get; init; }
    }
}
