using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Lessons;

/// <summary>
/// Carrega uma aula para alterar, já filtrada pelo dono, com <c>SELECT ... FOR UPDATE</c> (precisa de transação
/// aberta). A resposta do aluno e a exclusão pelo personal travam a mesma linha: uma espera a outra e a segunda já
/// lê o estado gravado pela primeira. Aula de outro usuário ou inexistente devolve nulo.
/// </summary>
public static class LessonAccess
{
    /// <summary>Aula de um vínculo do personal logado (perfil ativo).</summary>
    public static async Task<Lesson?> LockForPersonalAsync(
        this AppDbContext db, Guid personalUserId, Guid lessonId, CancellationToken cancellationToken)
    {
        // xmin é coluna de sistema (fora do *) e o EF precisa dela como token de concorrência. Sem composição sobre o
        // FromSql: o EF envia o SQL como está e o FOR UPDATE vale.
        var rows = await db.Lessons
            .FromSqlInterpolated($"""
                SELECT l.*, l.xmin FROM "Lesson" l
                JOIN "PersonalStudent" ps ON ps."Id" = l."PersonalStudentId"
                JOIN "PersonalProfile" pp ON pp."Id" = ps."PersonalId"
                WHERE l."Id" = {lessonId} AND pp."UserId" = {personalUserId} AND pp."IsActive"
                FOR UPDATE OF l
                """)
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    /// <summary>Aula de um vínculo cujo aluno é o usuário logado.</summary>
    public static async Task<Lesson?> LockForStudentAsync(
        this AppDbContext db, Guid studentUserId, Guid lessonId, CancellationToken cancellationToken)
    {
        var rows = await db.Lessons
            .FromSqlInterpolated($"""
                SELECT l.*, l.xmin FROM "Lesson" l
                JOIN "PersonalStudent" ps ON ps."Id" = l."PersonalStudentId"
                WHERE l."Id" = {lessonId} AND ps."StudentUserId" = {studentUserId}
                FOR UPDATE OF l
                """)
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    /// <summary>Auditoria de uma ação sobre a aula. O metadata não leva observação, motivos nem dados pessoais.</summary>
    public static void AddLessonAudit(
        this AppDbContext db, Guid actorUserId, string action, Guid lessonId, DateTime nowUtc, string? correlationId,
        object? metadata = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = nameof(Lesson),
            EntityId = lessonId,
            OccurredAtUtc = nowUtc,
            CorrelationId = correlationId,
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata),
            CreatedAtUtc = nowUtc
        });
}
