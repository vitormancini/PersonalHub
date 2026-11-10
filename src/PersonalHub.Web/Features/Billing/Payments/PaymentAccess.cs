using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Billing.Payments;

/// <summary>
/// Carrega um demonstrativo para mudar a situação do pagamento, já filtrado pelo dono, com <c>SELECT ... FOR UPDATE</c>
/// (precisa de transação aberta). Informar, confirmar, recusar e registrar travam a mesma linha: um espera o outro e o
/// segundo já lê a situação gravada pelo primeiro. Demonstrativo de outro usuário ou inexistente devolve nulo.
/// </summary>
public static class PaymentAccess
{
    /// <summary>
    /// Demonstrativo de um vínculo cujo aluno é o usuário logado. O vínculo pode estar inativo: o que já foi cobrado
    /// ainda pode ser pago.
    /// </summary>
    public static async Task<BillingPeriod?> LockPeriodForStudentAsync(
        this AppDbContext db, Guid studentUserId, Guid billingPeriodId, CancellationToken cancellationToken)
    {
        // xmin é coluna de sistema (fora do *) e o EF precisa dela como token de concorrência.
        var rows = await db.BillingPeriods
            .FromSqlInterpolated($"""
                SELECT bp.*, bp.xmin FROM "BillingPeriod" bp
                JOIN "PersonalStudent" ps ON ps."Id" = bp."PersonalStudentId"
                WHERE bp."Id" = {billingPeriodId} AND ps."StudentUserId" = {studentUserId}
                FOR UPDATE OF bp
                """)
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    /// <summary>Demonstrativo de um vínculo do personal logado (perfil ativo).</summary>
    public static async Task<BillingPeriod?> LockPeriodForPersonalAsync(
        this AppDbContext db, Guid personalUserId, Guid billingPeriodId, CancellationToken cancellationToken)
    {
        var rows = await db.BillingPeriods
            .FromSqlInterpolated($"""
                SELECT bp.*, bp.xmin FROM "BillingPeriod" bp
                JOIN "PersonalStudent" ps ON ps."Id" = bp."PersonalStudentId"
                JOIN "PersonalProfile" pp ON pp."Id" = ps."PersonalId"
                WHERE bp."Id" = {billingPeriodId} AND pp."UserId" = {personalUserId} AND pp."IsActive"
                FOR UPDATE OF bp
                """)
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    /// <summary>
    /// Auditoria de uma ação sobre o pagamento. O metadata leva só o demonstrativo: nada da observação do aluno nem da
    /// mensagem do personal.
    /// </summary>
    public static void AddPaymentAudit(
        this AppDbContext db, Guid actorUserId, string action, Payment payment, DateTime nowUtc, string? correlationId) =>
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = nameof(Payment),
            EntityId = payment.Id,
            OccurredAtUtc = nowUtc,
            CorrelationId = correlationId,
            Metadata = JsonSerializer.Serialize(new { payment.BillingPeriodId }),
            CreatedAtUtc = nowUtc
        });
}
