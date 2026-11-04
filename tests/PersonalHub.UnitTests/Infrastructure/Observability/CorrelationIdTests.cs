using System.Diagnostics;
using PersonalHub.Web.Infrastructure.Observability;

namespace PersonalHub.UnitTests.Infrastructure.Observability;

public class CorrelationIdTests
{
    [Theory]
    [InlineData("abc-123")]
    [InlineData("A")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    public void IsValid_AcceptsLettersDigitsAndHyphen(string value)
    {
        Assert.True(CorrelationId.IsValid(value));
    }

    [Fact]
    public void IsValid_AcceptsExactlyMaxLength()
    {
        Assert.True(CorrelationId.IsValid(new string('a', CorrelationId.MaxLength)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tem espaço")]
    [InlineData("quebra\nde-linha")]
    [InlineData("<script>")]
    [InlineData("a_b")]
    [InlineData("ação")]
    public void IsValid_RejectsUnsafeValues(string? value)
    {
        Assert.False(CorrelationId.IsValid(value));
    }

    [Fact]
    public void IsValid_RejectsTooLong()
    {
        Assert.False(CorrelationId.IsValid(new string('a', CorrelationId.MaxLength + 1)));
    }

    [Fact]
    public void Resolve_KeepsValidIncomingValue()
    {
        Assert.Equal("cliente-42", CorrelationId.Resolve("cliente-42", activity: null));
    }

    [Fact]
    public void Resolve_UsesTraceIdWhenIncomingIsInvalid()
    {
        using var activity = new Activity("test").SetIdFormat(ActivityIdFormat.W3C).Start();

        Assert.Equal(activity.TraceId.ToHexString(), CorrelationId.Resolve("inválido!", activity));
    }

    [Fact]
    public void Resolve_GeneratesValidIdWithoutIncomingOrActivity()
    {
        var resolved = CorrelationId.Resolve(null, activity: null);

        Assert.True(CorrelationId.IsValid(resolved));
    }
}
