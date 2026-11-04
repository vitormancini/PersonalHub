namespace PersonalHub.Web.Domain.Students;

/// <summary>Convite de uso único (D-016). Só o hash SHA-256 do token é gravado.</summary>
public class StudentInvite
{
    public Guid Id { get; set; }
    public Guid PersonalStudentId { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
