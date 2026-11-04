namespace PersonalHub.Web.Domain.Notifications;

/// <summary>
/// Intenção de notificar, gravada na mesma transação da operação de negócio e enviada depois por um worker.
/// </summary>
public class NotificationOutbox
{
    public Guid Id { get; set; }
    public required string EventType { get; set; }
    public Guid RecipientUserId { get; set; }
    public string? RecipientPhone { get; set; }

    /// <summary>JSON sem segredos nem dados pessoais desnecessários.</summary>
    public required string Payload { get; set; }

    public NotificationChannel Channel { get; set; }
    public NotificationStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? ProviderMessageId { get; set; }

    /// <summary>Mensagem de erro sanitizada.</summary>
    public string? LastError { get; set; }

    /// <summary>Chave única do aviso, por exemplo <c>lesson-created:&lt;id&gt;</c>.</summary>
    public required string IdempotencyKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
