using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Students.ListStudents;

/// <summary>Filtro por situação, como aparece na query string (?situacao=).</summary>
public enum StudentFilter
{
    Ativos,
    Convidados,
    Inativos,
    Todos
}

public sealed record StudentListItem(
    Guid Id,
    string Name,
    PersonalStudentStatus Status,
    decimal LessonPrice,
    short BillingDueDay,
    DateTime? EndedAtUtc,
    InviteStatus? Invite,
    DateTime? InviteExpiresAtUtc);

public sealed record StudentListView(
    IReadOnlyList<StudentListItem> Items,
    IReadOnlyDictionary<StudentFilter, int> Counts,
    bool HasAnyStudent);

/// <summary>Alunos do personal logado, em ordem alfabética, com contagem por situação.</summary>
public sealed class ListStudentsQuery(AppDbContext db, TimeProvider clock)
{
    public const int SearchMaxLength = 100;

    private static readonly StringComparer NameOrder = StringComparer.Create(StudentDisplay.PtBr, CompareOptions.IgnoreCase);

    public async Task<StudentListView> GetAsync(
        Guid personalUserId, StudentFilter filter, string? search, CancellationToken cancellationToken)
    {
        var links = db.PersonalStudents
            .Join(db.PersonalProfiles.Where(p => p.UserId == personalUserId && p.IsActive),
                s => s.PersonalId, p => p.Id, (s, p) => s);

        var hasAny = await links.AnyAsync(cancellationToken);

        var term = (search ?? string.Empty).Trim();
        if (term.Length > SearchMaxLength)
        {
            term = term[..SearchMaxLength];
        }

        if (term.Length > 0)
        {
            // ILIKE com os curingas do usuário escapados: "50%" procura o texto "50%", não "50 seguido de qualquer coisa".
            var pattern = "%" + term.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
            links = links.Where(s => EF.Functions.ILike(s.StudentName, pattern, @"\"));
        }

        var byStatus = await links
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int CountOf(PersonalStudentStatus status) => byStatus.Where(x => x.Status == status).Sum(x => x.Count);
        var counts = new Dictionary<StudentFilter, int>
        {
            [StudentFilter.Ativos] = CountOf(PersonalStudentStatus.Active),
            [StudentFilter.Convidados] = CountOf(PersonalStudentStatus.Invited),
            [StudentFilter.Inativos] = CountOf(PersonalStudentStatus.Inactive),
            [StudentFilter.Todos] = byStatus.Sum(x => x.Count)
        };

        links = filter switch
        {
            StudentFilter.Ativos => links.Where(s => s.Status == PersonalStudentStatus.Active),
            StudentFilter.Convidados => links.Where(s => s.Status == PersonalStudentStatus.Invited),
            StudentFilter.Inativos => links.Where(s => s.Status == PersonalStudentStatus.Inactive),
            _ => links
        };

        var rows = await links
            .Select(s => new
            {
                s.Id,
                s.StudentName,
                s.Status,
                s.LessonPrice,
                s.BillingDueDay,
                s.EndedAtUtc,
                Invite = db.StudentInvites
                    .Where(i => i.PersonalStudentId == s.Id)
                    .OrderByDescending(i => i.CreatedAtUtc)
                    .Select(i => new { i.ExpiresAtUtc, i.UsedAtUtc, i.RevokedAtUtc })
                    .FirstOrDefault()
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Ordena em memória com as regras do português (acentos e maiúsculas); a lista de um personal é pequena.
        var now = clock.GetUtcNow().UtcDateTime;
        var items = rows
            .OrderBy(r => r.StudentName, NameOrder)
            .Select(r => new StudentListItem(r.Id, r.StudentName, r.Status, r.LessonPrice, r.BillingDueDay, r.EndedAtUtc,
                r.Status == PersonalStudentStatus.Invited
                    ? InviteStatusRules.Of(r.Invite?.ExpiresAtUtc, r.Invite?.UsedAtUtc, r.Invite?.RevokedAtUtc, now)
                    : null,
                r.Invite?.ExpiresAtUtc))
            .ToList();

        return new StudentListView(items, counts, hasAny);
    }
}
