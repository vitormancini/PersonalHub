using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Features.Lessons;

namespace PersonalHub.UnitTests.Features.Lessons;

public class LessonDisplayTests
{
    [Theory]
    [InlineData(LessonStatus.Pending, false, "Aguardando", "ph-pill-pending")]
    [InlineData(LessonStatus.Approved, false, "Confirmada", "ph-pill-success")]
    [InlineData(LessonStatus.Rejected, false, "Recusada", "ph-pill-danger")]
    [InlineData(LessonStatus.Approved, true, "Excluída", "ph-pill-muted")]
    public void Status_LabelsFromD038(LessonStatus status, bool deleted, string label, string pill)
    {
        Assert.Equal(label, LessonDisplay.StatusLabel(status, deleted));
        Assert.Equal(pill, LessonDisplay.StatusPillClass(status, deleted));
    }

    [Fact]
    public void Month_IsCapitalizedPortuguese() =>
        Assert.Equal("Novembro de 2026", LessonDisplay.Month(new DateOnly(2026, 11, 1)));

    [Fact]
    public void DayHeading_TodayAndOtherDays()
    {
        var today = new DateOnly(2026, 11, 4);

        Assert.Equal("Hoje, quarta 4", LessonDisplay.DayHeading(today, today));
        Assert.Equal("Terça 3", LessonDisplay.DayHeading(today.AddDays(-1), today));
        Assert.Equal("Sábado 31", LessonDisplay.DayHeading(new DateOnly(2026, 10, 31), today));
    }

    [Fact]
    public void Dates_UseSaoPauloTime()
    {
        var utc = new DateTime(2026, 11, 5, 1, 30, 0, DateTimeKind.Utc);

        Assert.Equal("22:30", LessonDisplay.Time(utc));
        Assert.Equal("Quarta-feira, 4 de novembro de 2026", LessonDisplay.LongDate(utc));
        Assert.Equal("Quarta, 4 de novembro", LessonDisplay.ShortDate(utc));
        Assert.Equal("04/11 às 22:30", LessonDisplay.DayMonthTime(utc));
    }

    [Fact]
    public void Summary_WithAndWithoutDuration()
    {
        Assert.Equal("60 min · R$ 80,00", LessonDisplay.Summary(60, 80m));
        Assert.Equal("R$ 80,00", LessonDisplay.Summary(null, 80m));
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "Ana" }, "Ana")]
    [InlineData(new[] { "Ana", "Bruno" }, "Ana e Bruno")]
    [InlineData(new[] { "Ana", "Bruno", "Carla" }, "Ana, Bruno e Carla")]
    public void JoinNames_InPortuguese(string[] names, string expected) =>
        Assert.Equal(expected, LessonDisplay.JoinNames(names));
}
