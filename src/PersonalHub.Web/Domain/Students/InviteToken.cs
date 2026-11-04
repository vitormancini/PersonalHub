using System.Buffers.Text;
using System.Security.Cryptography;

namespace PersonalHub.Web.Domain.Students;

/// <summary>
/// Token do convite (D-016): 32 bytes aleatórios em base64url (43 caracteres) no link; no banco, só o
/// SHA-256 dos bytes. Quem lê o banco não consegue montar um link válido.
/// </summary>
public static class InviteToken
{
    public const int ByteLength = 32;
    public const int TextLength = 43;
    public static readonly TimeSpan Validity = TimeSpan.FromDays(7);

    public static (string Token, byte[] Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(ByteLength);
        return (Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    /// <summary>Hash do token recebido na URL, ou nulo se o texto não tem o formato de um token.</summary>
    public static byte[]? TryHash(string? token)
    {
        if (token is null || token.Length != TextLength || !Base64Url.IsValid(token))
        {
            return null;
        }

        Span<byte> bytes = stackalloc byte[ByteLength];
        if (Base64Url.DecodeFromChars(token, bytes) != ByteLength)
        {
            return null;
        }

        return SHA256.HashData(bytes);
    }
}
