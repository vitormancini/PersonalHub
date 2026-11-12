using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace PersonalHub.Web.Features.Students.AcceptInvite;

/// <summary>
/// Prova de que o celular do convite foi confirmado (D-057): valor protegido pelo Data Protection, preso ao convite e
/// ao celular, válido por 15 minutos. Vai num campo oculto do formulário da conta e é conferido de novo no POST, então
/// não há como criar a conta ou aceitar o convite sem o código. Trocar o celular ou gerar outro convite invalida a prova.
/// </summary>
public sealed class InvitePhoneProof(IDataProtectionProvider provider)
{
    public static readonly TimeSpan Validity = TimeSpan.FromMinutes(15);

    private readonly IDataProtector _protector = provider.CreateProtector("PersonalHub.Invite.PhoneVerified.v1");

    public string Create(Guid inviteId, string phone, DateTime nowUtc) =>
        _protector.Protect(string.Join('|', inviteId.ToString("N"), phone,
            (nowUtc + Validity).Ticks.ToString(CultureInfo.InvariantCulture)));

    public bool IsValid(string? proof, Guid inviteId, string phone, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(proof) || proof.Length > 1000)
        {
            return false;
        }

        string text;
        try
        {
            text = _protector.Unprotect(proof);
        }
        catch (CryptographicException)
        {
            return false;
        }

        var parts = text.Split('|');
        return parts.Length == 3
               && parts[0] == inviteId.ToString("N")
               && parts[1] == phone
               && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
               && nowUtc.Ticks < ticks;
    }
}
