using System.Security.Cryptography;
using PersonalHub.Web.Domain.Students;

namespace PersonalHub.UnitTests.Domain.Students;

public class InviteTokenTests
{
    [Fact]
    public void Generate_ReturnsBase64UrlTokenAndItsSha256()
    {
        var (token, hash) = InviteToken.Generate();

        Assert.Equal(InviteToken.TextLength, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, InviteToken.TryHash(token));
    }

    [Fact]
    public void Generate_IsRandom()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => InviteToken.Generate().Token).ToHashSet();

        Assert.Equal(100, tokens.Count);
    }

    [Fact]
    public void Hash_IsOverTheRandomBytes_NotTheText()
    {
        var (token, hash) = InviteToken.Generate();

        Assert.False(hash.AsSpan().SequenceEqual(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]   // 42 caracteres
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 44 caracteres
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]  // caractere de base64 comum, não base64url
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void TryHash_RejectsMalformedTokens(string? token)
    {
        Assert.Null(InviteToken.TryHash(token));
    }
}
