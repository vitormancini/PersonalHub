namespace PersonalHub.Web.Domain.PhoneVerification;

/// <summary>
/// Código de 6 dígitos enviado por WhatsApp (D-056). Só o HMAC do código é gravado. O escopo é o vínculo (convite) ou
/// a conta (confirmação de celular e senha); os limites de envio contam por celular e finalidade.
/// </summary>
public class PhoneVerificationCode
{
    public Guid Id { get; set; }
    public PhoneCodePurpose Purpose { get; set; }
    public Guid? UserId { get; set; }
    public Guid? PersonalStudentId { get; set; }
    public required string PhoneE164 { get; set; }

    /// <summary>HMAC-SHA256 do código com a chave do servidor (<c>PhoneVerification:HashKey</c>).</summary>
    public required byte[] CodeHash { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
    public int Attempts { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>Substituído por um código mais novo: deixa de valer.</summary>
    public DateTime? ReplacedAtUtc { get; set; }

    /// <summary>Bloqueio de 15 minutos depois de 5 tentativas erradas ou do limite de envios.</summary>
    public DateTime? LockedUntilUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public bool IsOpen(DateTime nowUtc) => ConsumedAtUtc is null && ReplacedAtUtc is null && ExpiresAtUtc > nowUtc;
}
