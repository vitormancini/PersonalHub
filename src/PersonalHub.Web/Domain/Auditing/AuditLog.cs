namespace PersonalHub.Web.Domain.Auditing;

/// <summary>Trilha de ações críticas. Não guardar senhas, tokens ou dados pessoais desnecessários.</summary>
public class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>Nulo quando a ação é do sistema (por exemplo, um job).</summary>
    public Guid? ActorUserId { get; set; }

    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>JSON minimizado e sanitizado.</summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
