using PersonalHub.Web.Features.Authentication.ProvisionPersonal;

namespace PersonalHub.UnitTests.Features.Authentication;

public class ProvisionPersonalCommandTests
{
    [Fact]
    public void IsInvoked_OnlyWithCommandAsFirstArgument()
    {
        Assert.True(ProvisionPersonalCommand.IsInvoked(["provision-personal", "--email", "a@b.c"]));
        Assert.False(ProvisionPersonalCommand.IsInvoked([]));
        Assert.False(ProvisionPersonalCommand.IsInvoked(["--urls", "http://localhost:5000"]));
    }

    [Fact]
    public void TryParse_ReadsAllOptions()
    {
        var ok = ProvisionPersonalCommand.TryParse(
            ["--email", "ana@example.test", "--name", "Ana Souza", "--phone", "+5511999990000"], out var args, out var error);

        Assert.True(ok, error);
        Assert.Equal(new ProvisionPersonalArguments("ana@example.test", "Ana Souza", "+5511999990000"), args);
    }

    [Fact]
    public void TryParse_PhoneIsOptional()
    {
        Assert.True(ProvisionPersonalCommand.TryParse(["--name", "Ana", "--email", "ana@example.test"], out var args, out _));
        Assert.Null(args!.Phone);
    }

    [Theory]
    [InlineData("--email", "ana@example.test")]
    [InlineData("--name", "Ana")]
    [InlineData("--email", "ana@example.test", "--name")]
    [InlineData("--email", "--name", "Ana")]
    [InlineData("--email", "ana@example.test", "--name", "Ana", "--password", "x")]
    public void TryParse_RejectsMissingOrUnknownOptions(params string[] input)
    {
        Assert.False(ProvisionPersonalCommand.TryParse(input, out var args, out var error));
        Assert.Null(args);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
