namespace PersonalHub.Web.Domain.Students;

/// <summary>Perfil de um personal trainer. Criado somente pelo script de provisionamento (D-027).</summary>
public class PersonalProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
