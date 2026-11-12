using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PersonalHub.Web.Domain.PhoneVerification;

namespace PersonalHub.Web.Features.PhoneVerification;

public sealed class PhoneVerificationOptions
{
    public const string Section = "PhoneVerification";

    /// <summary>
    /// Chave do HMAC dos códigos, em base64 (32 bytes ou mais). Obrigatória fora de Development e Testing; sem ela,
    /// nesses ambientes, cada processo sorteia uma chave (códigos pendentes deixam de valer ao reiniciar).
    /// </summary>
    public string? HashKey { get; set; }

    public byte[]? KeyBytes()
    {
        try
        {
            var bytes = string.IsNullOrWhiteSpace(HashKey) ? null : Convert.FromBase64String(HashKey);
            return bytes is { Length: >= 32 } ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>
/// HMAC-SHA256 do código com uma chave do servidor (D-056). Com 1 milhão de códigos possíveis, um hash simples seria
/// revertido na hora por quem lesse o banco; sem a chave, o HMAC não é. A finalidade e o escopo entram no texto, então o
/// hash de um código não serve para outro fluxo.
/// </summary>
public sealed class PhoneCodeHasher
{
    private readonly byte[] _key;

    public PhoneCodeHasher(IOptions<PhoneVerificationOptions> options) =>
        _key = options.Value.KeyBytes() ?? RandomNumberGenerator.GetBytes(32);

    public byte[] Hash(PhoneCodePurpose purpose, Guid scopeId, string code) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{purpose}:{scopeId:N}:{code}"));

    public bool Matches(byte[] storedHash, PhoneCodePurpose purpose, Guid scopeId, string code) =>
        CryptographicOperations.FixedTimeEquals(storedHash, Hash(purpose, scopeId, code));

    /// <summary>Código uniforme de 6 dígitos (000000 a 999999) do gerador criptográfico.</summary>
    public static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}
