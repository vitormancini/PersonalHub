using PersonalHub.Web.Features.Lessons.RegisterLesson;

namespace PersonalHub.UnitTests.Features.Lessons;

public class RegisterLessonFieldsTests
{
    // 23:30 de 04/11/2026 em Brasília.
    private static readonly DateTime Now = new(2026, 11, 5, 2, 30, 0, DateTimeKind.Utc);

    private static RegisterLessonInput Input(string date = "2026-11-04", string time = "23:00") => new()
    {
        StudentId = Guid.NewGuid().ToString(),
        Date = date,
        Time = time,
        Duration = "60",
        Notes = "  Treino de pernas  "
    };

    [Fact]
    public void Valid_ConvertsSaoPauloTimeToUtc()
    {
        var input = Input();

        var errors = RegisterLessonFields.Validate(input, Now, out var data);

        Assert.Empty(errors);
        Assert.Equal(new DateTime(2026, 11, 5, 2, 0, 0, DateTimeKind.Utc), data.OccurredAtUtc);
        Assert.Equal((short)60, data.DurationMinutes);
        Assert.Equal("Treino de pernas", data.Notes);
    }

    [Theory]
    [InlineData("2026-11-05", "00:10")] // já é dia 05 em UTC, mas ainda 04 em Brasília
    [InlineData("2026-11-04", "23:31")]
    [InlineData("2026-12-01", "08:00")]
    public void Future_InSaoPaulo_IsRejected(string date, string time)
    {
        var errors = RegisterLessonFields.Validate(Input(date, time), Now, out _);

        Assert.Equal(RegisterLessonFields.FutureMessage, errors[nameof(RegisterLessonInput.Date)]);
    }

    [Theory]
    [InlineData("2026-11-04", "23:30")]
    [InlineData("2025-01-15", "07:00")]
    public void NowOrPast_IsAccepted(string date, string time) =>
        Assert.Empty(RegisterLessonFields.Validate(Input(date, time), Now, out _));

    [Fact]
    public void Seconds_AreDropped()
    {
        RegisterLessonFields.Validate(Input(time: "22:15:59"), Now, out var data);

        Assert.Equal(new DateTime(2026, 11, 5, 1, 15, 0, DateTimeKind.Utc), data.OccurredAtUtc);
    }

    [Fact]
    public void Empty_ReportsEveryRequiredField()
    {
        var errors = RegisterLessonFields.Validate(new RegisterLessonInput(), Now, out _);

        Assert.Equal("Escolha o aluno.", errors[nameof(RegisterLessonInput.StudentId)]);
        Assert.Equal("Informe a data da aula.", errors[nameof(RegisterLessonInput.Date)]);
        Assert.Equal("Informe o horário da aula.", errors[nameof(RegisterLessonInput.Time)]);
        Assert.False(errors.ContainsKey(nameof(RegisterLessonInput.Duration)));
    }

    [Theory]
    [InlineData("50")]
    [InlineData("-30")]
    [InlineData("abc")]
    public void Duration_OutsidePresets_IsRejected(string duration)
    {
        var input = Input();
        input.Duration = duration;

        Assert.Equal("Escolha 30, 45, 60 ou 90 minutos.",
            RegisterLessonFields.Validate(input, Now, out _)[nameof(RegisterLessonInput.Duration)]);
    }

    [Fact]
    public void Notes_Over500_IsRejected()
    {
        var input = Input();
        input.Notes = new string('a', 501);

        Assert.Contains(nameof(RegisterLessonInput.Notes), RegisterLessonFields.Validate(input, Now, out _).Keys);
    }

    [Fact]
    public void DuplicateKey_DependsOnLinkAndInstant()
    {
        var link = Guid.NewGuid();
        var at = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(RegisterLessonFields.DuplicateKey(link, at), RegisterLessonFields.DuplicateKey(link, at));
        Assert.NotEqual(RegisterLessonFields.DuplicateKey(link, at), RegisterLessonFields.DuplicateKey(link, at.AddMinutes(1)));
        Assert.NotEqual(RegisterLessonFields.DuplicateKey(link, at), RegisterLessonFields.DuplicateKey(Guid.NewGuid(), at));
    }
}
