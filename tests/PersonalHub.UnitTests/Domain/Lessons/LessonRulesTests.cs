using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Lessons;

namespace PersonalHub.UnitTests.Domain.Lessons;

public class LessonRulesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData((short)30, true)]
    [InlineData((short)45, true)]
    [InlineData((short)60, true)]
    [InlineData((short)90, true)]
    [InlineData((short)0, false)]
    [InlineData((short)50, false)]
    [InlineData((short)120, false)]
    public void Duration_OnlyPresetsOrNone(short? minutes, bool expected) =>
        Assert.Equal(expected, LessonRules.IsAllowedDuration(minutes));

    [Fact]
    public void Future_IsComparedAsInstant()
    {
        var now = new DateTime(2026, 11, 5, 2, 30, 0, DateTimeKind.Utc);

        Assert.False(LessonRules.IsInFuture(now, now));
        Assert.False(LessonRules.IsInFuture(now.AddMinutes(-1), now));
        Assert.True(LessonRules.IsInFuture(now.AddMinutes(1), now));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Aula extra ", "Aula extra")]
    public void NormalizeText_TrimsAndNullsEmpty(string? text, string? expected) =>
        Assert.Equal(expected, LessonRules.NormalizeText(text));

    [Fact]
    public void SaoPaulo_ToUtc_UsesBrasiliaOffset()
    {
        // Sem horário de verão desde 2019: UTC-3.
        Assert.Equal(new DateTime(2026, 11, 5, 2, 10, 0, DateTimeKind.Utc),
            SaoPaulo.ToUtc(new DateOnly(2026, 11, 4), new TimeOnly(23, 10)));
    }

    [Fact]
    public void SaoPaulo_ToUtc_SkippedDaylightSavingHour_IsNull() =>
        // 04/11/2018: o relógio pulou de 00:00 para 01:00.
        Assert.Null(SaoPaulo.ToUtc(new DateOnly(2018, 11, 4), new TimeOnly(0, 30)));

    [Fact]
    public void SaoPaulo_MonthRange_StartsAtLocalMidnight()
    {
        var (start, end) = SaoPaulo.MonthRangeUtc(2026, 11);

        Assert.Equal(new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc), end);
    }
}
