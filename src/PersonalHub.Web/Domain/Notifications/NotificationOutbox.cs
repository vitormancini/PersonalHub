namespace PersonalHub.Web.Domain.Notifications;

/// <summary>
/// Intenção de notificar, gravada na mesma transação da operação de negócio e enviada depois por um worker
/// (DOMAIN_AND_DATABASE §4.9). <see cref="EventType"/> é o nome do template do WhatsApp.
/// </summary>
public class NotificationOutbox
{
    public Guid Id { get; set; }

    /// <summary>Nome do template (por exemplo, <c>ph_nova_aula</c>).</summary>
    public required string EventType { get; set; }

    /// <summary>Nulo no código do convite: o aluno ainda não tem conta.</summary>
    public Guid? RecipientUserId { get; set; }

    /// <summary>Celular E.164 no momento do enfileiramento. Nulo quando o destinatário não tem celular confirmado.</summary>
    public string? RecipientPhone { get; set; }

    /// <summary>
    /// JSON com os parâmetros do template e o sufixo do botão. Nos códigos, o código vai protegido pelo Data
    /// Protection e o payload é apagado quando a mensagem sai ou falha de vez.
    /// </summary>
    public required string Payload { get; set; }

    public NotificationChannel Channel { get; set; }
    public NotificationStatus Status { get; set; }

    /// <summary>Código de verificação: ignora o horário de silêncio e expira junto com o código.</summary>
    public bool IsAuthentication { get; set; }

    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }

    /// <summary>Fim do prazo de quem pegou a linha para enviar (<c>Processing</c>). Vencido, outro worker retoma.</summary>
    public DateTime? LeaseExpiresAtUtc { get; set; }

    /// <summary>Depois disso não adianta mais enviar (código vencido).</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }
    public string? ProviderMessageId { get; set; }

    /// <summary>Situação de entrega informada pelo webhook do provedor.</summary>
    public NotificationDeliveryStatus? DeliveryStatus { get; set; }

    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }

    /// <summary>Erro sanitizado: só códigos (<c>meta:131026</c>, <c>http:503</c>, <c>sem-celular</c>), nunca texto do provedor.</summary>
    public string? LastError { get; set; }

    /// <summary>Chave única do aviso, por exemplo <c>lesson-created:&lt;id&gt;</c>.</summary>
    public required string IdempotencyKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
