using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Notifications;

/// <summary>Um aviso a enfileirar. <see cref="Phone"/> nulo: o worker encerra a linha como <c>sem-celular</c>.</summary>
public sealed record NotificationRequest(
    string Template,
    string IdempotencyKey,
    Guid? RecipientUserId,
    string? Phone,
    NotificationPayload Payload,
    bool IsAuthentication = false,
    DateTime? ExpiresAtUtc = null);

/// <summary>
/// Grava o aviso na outbox pelo mesmo <see cref="AppDbContext"/> da operação de negócio: o <c>SaveChanges</c> e o
/// commit da operação levam o aviso junto, e um rollback não deixa linha nenhuma. Nunca envia nada aqui.
/// </summary>
public static class NotificationOutboxWriter
{
    /// <summary>
    /// Adiciona o aviso ao contexto (sem salvar). Devolve falso quando a chave já existe: o mesmo aviso nunca é
    /// enfileirado duas vezes. O índice único da chave segura uma corrida que escape desta checagem.
    /// </summary>
    public static async Task<bool> EnqueueNotificationAsync(
        this AppDbContext db, NotificationRequest request, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (db.NotificationOutbox.Local.Any(o => o.IdempotencyKey == request.IdempotencyKey) ||
            await db.NotificationOutbox.AnyAsync(o => o.IdempotencyKey == request.IdempotencyKey, cancellationToken))
        {
            return false;
        }

        db.NotificationOutbox.Add(new NotificationOutbox
        {
            Id = Guid.NewGuid(),
            EventType = request.Template,
            RecipientUserId = request.RecipientUserId,
            RecipientPhone = request.Phone,
            Payload = request.Payload.ToJson(),
            Channel = NotificationChannel.WhatsApp,
            Status = NotificationStatus.Pending,
            IsAuthentication = request.IsAuthentication,
            NextAttemptAtUtc = NotificationSchedule.SendAt(nowUtc, request.IsAuthentication),
            ExpiresAtUtc = request.ExpiresAtUtc,
            IdempotencyKey = request.IdempotencyKey,
            CreatedAtUtc = nowUtc
        });
        return true;
    }
}
