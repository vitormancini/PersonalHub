using PersonalHub.Web.Infrastructure.WhatsApp;

namespace PersonalHub.UnitTests.Infrastructure.WhatsApp;

public class WhatsAppOptionsTests
{
    private static WhatsAppOptions Meta() => new()
    {
        Provider = "Meta",
        ApiVersion = "v23.0",
        PhoneNumberId = "123456789012345",
        AccessToken = "token"
    };

    [Fact]
    public void Log_IsValidOutsideProduction_AndRefusedInProduction()
    {
        Assert.Empty(new WhatsAppOptions().Validate(isProduction: false));
        Assert.Contains(new WhatsAppOptions().Validate(isProduction: true), e => e.Contains("Production"));
    }

    [Fact]
    public void Meta_WithCredentials_IsValid()
    {
        Assert.Empty(Meta().Validate(isProduction: true));
    }

    [Fact]
    public void Meta_WithoutTokenOrPhoneNumberId_IsInvalid_WithoutEchoingSecrets()
    {
        var options = Meta();
        options.AccessToken = "";
        options.PhoneNumberId = "abc";

        var errors = options.Validate(isProduction: true);

        Assert.Contains(errors, e => e.Contains("AccessToken"));
        Assert.Contains(errors, e => e.Contains("PhoneNumberId"));
    }

    [Theory]
    [InlineData("23.0")]
    [InlineData("v23")]
    [InlineData("v23.0/../")]
    public void Meta_WithBadApiVersion_IsInvalid(string version)
    {
        var options = Meta();
        options.ApiVersion = version;

        Assert.Contains(options.Validate(isProduction: false), e => e.Contains("ApiVersion"));
    }

    [Fact]
    public void Meta_WithAppSecretButNoVerifyToken_IsInvalid()
    {
        var options = Meta();
        options.AppSecret = "segredo";

        Assert.Contains(options.Validate(isProduction: false), e => e.Contains("WebhookVerifyToken"));
    }

    [Fact]
    public void UnknownProvider_IsInvalid()
    {
        Assert.NotEmpty(new WhatsAppOptions { Provider = "Twilio" }.Validate(isProduction: false));
    }

    [Theory]
    [InlineData("+5511987652468", "+55•••••••2468")]
    [InlineData(null, "••••")]
    public void PhoneLogMask_KeepsOnlyLastDigits(string? phone, string expected)
    {
        Assert.Equal(expected, PhoneLogMask.Mask(phone));
    }
}
