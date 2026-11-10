using PersonalHub.Web.Domain;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Lessons;

namespace PersonalHub.UnitTests.Domain.Billing;

/// <summary>Regras do fechamento mensal (D-019, D-020, D-023, D-025, D-043 a D-046).</summary>
public class BillingClosingTests
{
    private static readonly DateOnly October = new(2026, 10, 1);

    /// <summary>Instante UTC de um horário de Brasília.</summary>
    private static DateTime Sp(int year, int month, int day, int hour = 10, int minute = 0) =>
        SaoPaulo.ToUtc(new DateOnly(year, month, day), new TimeOnly(hour, minute))!.Value;

    private static ClosingLesson Approved(DateTime occurredAtUtc, decimal price = 100m, DateTime? respondedAtUtc = null) =>
        new(Guid.NewGuid(), occurredAtUtc, price, LessonStatus.Approved, respondedAtUtc ?? occurredAtUtc.AddHours(2), null);

    // ---------------------------------------------------------------- Calendário

    [Theory]
    [InlineData(2026, 10, 10, 2026, 11, 10)]
    [InlineData(2027, 1, 31, 2027, 2, 28)]   // fevereiro comum
    [InlineData(2028, 1, 31, 2028, 2, 29)]   // fevereiro bissexto
    [InlineData(2027, 3, 31, 2027, 4, 30)]   // mês de 30 dias
    [InlineData(2026, 12, 5, 2027, 1, 5)]    // virada do ano
    [InlineData(2027, 4, 31, 2027, 5, 31)]   // dia 31 existe no mês do vencimento
    public void ClosingDate_IsTheDueDateOfTheNextMonth(int refYear, int refMonth, int dueDay, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), BillingClosing.ClosingDate(new DateOnly(refYear, refMonth, 1), dueDay));
    }

    [Theory]
    [InlineData("2026-11-09", 10, "2026-09-01")] // véspera do vencimento: outubro ainda não fechou
    [InlineData("2026-11-10", 10, "2026-10-01")] // no dia do vencimento, outubro fecha
    [InlineData("2026-11-30", 10, "2026-10-01")]
    [InlineData("2027-02-27", 31, "2026-12-01")] // dia 31 em fevereiro vira 28
    [InlineData("2027-02-28", 31, "2027-01-01")]
    [InlineData("2027-01-05", 5, "2026-12-01")]  // virada do ano
    public void LatestDueReferenceMonth_FollowsTheDueDay(string today, int dueDay, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), BillingClosing.LatestDueReferenceMonth(DateOnly.Parse(today), dueDay));
    }

    [Fact]
    public void DueReferenceMonth_IsNullWhenAlreadyClosed()
    {
        var today = new DateOnly(2026, 11, 15);

        Assert.Equal(October, BillingClosing.DueReferenceMonth(today, 10, null));
        Assert.Equal(October, BillingClosing.DueReferenceMonth(today, 10, new DateOnly(2026, 9, 1)));
        Assert.Null(BillingClosing.DueReferenceMonth(today, 10, October));
    }

    [Fact]
    public void DueReferenceMonth_RecoversOnlyTheLatestMonthAfterDowntime()
    {
        // App fora do ar de 01/11 a 20/12: só fecha novembro, que leva também as aulas de outubro (D-043, D-047).
        Assert.Equal(new DateOnly(2026, 11, 1), BillingClosing.DueReferenceMonth(new DateOnly(2026, 12, 20), 10, new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public void DueReferenceMonth_NeverGoesBackBeforeAnExistingStatement()
    {
        // Vencimento trocado de 5 para 20 depois de outubro já ter fechado em 05/11: setembro não é reaberto.
        Assert.Null(BillingClosing.DueReferenceMonth(new DateOnly(2026, 11, 9), 20, October));
    }

    [Theory]
    [InlineData("2026-11-09", 10, "2026-10-01", "2026-11-10")]
    [InlineData("2026-11-10", 10, "2026-11-01", "2026-12-10")]
    [InlineData("2027-01-30", 31, "2026-12-01", "2027-01-31")]
    public void NextClosing_IsTheFirstClosingAfterToday(string today, int dueDay, string reference, string closing)
    {
        var (month, date) = BillingClosing.NextClosing(DateOnly.Parse(today), dueDay);

        Assert.Equal(DateOnly.Parse(reference), month);
        Assert.Equal(DateOnly.Parse(closing), date);
    }

    [Fact]
    public void NextClosing_SkipsMonthsAlreadyIssued()
    {
        // Outubro fechou em 05/11 e o vencimento passou para o dia 20: o próximo é novembro, em 20/12 (D-036).
        var (month, date) = BillingClosing.NextClosing(new DateOnly(2026, 11, 9), 20, October);

        Assert.Equal(new DateOnly(2026, 11, 1), month);
        Assert.Equal(new DateOnly(2026, 12, 20), date);
    }

    [Fact]
    public void CutoffUtc_IsMidnightInSaoPaulo()
    {
        Assert.Equal(new DateTime(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc), BillingClosing.CutoffUtc(new DateOnly(2026, 11, 10)));
    }

    // ---------------------------------------------------------------- O que entra

    [Fact]
    public void Compose_ChargesApprovedLessonsOfTheMonthWithFrozenPriceAndDescription()
    {
        var first = Approved(Sp(2026, 10, 5, 7), 80m);
        var second = Approved(Sp(2026, 10, 12, 18, 30), 80m);

        var result = BillingClosing.Compose(October, 10, [second, first], null)!;

        Assert.Equal(October, result.ReferenceMonth);
        Assert.Equal(new DateOnly(2026, 11, 10), result.DueDate);
        Assert.Equal(160m, result.TotalAmount);
        Assert.Equal(0m, result.CarryOut);
        Assert.Equal(2, result.LessonCount);
        Assert.Equal(["Aula de 05/10 às 07:00", "Aula de 12/10 às 18:30"], result.Items.Select(i => i.Description));
        Assert.All(result.Items, i => Assert.Equal(BillingPeriodItemKind.Lesson, i.Kind));
        Assert.Equal([first.Id, second.Id], result.Items.Select(i => i.LessonId!.Value));
    }

    [Fact]
    public void Compose_UsesEachLessonPrice()
    {
        var result = BillingClosing.Compose(October, 10, [Approved(Sp(2026, 10, 1), 80m), Approved(Sp(2026, 10, 20), 95.50m)], null)!;

        Assert.Equal(175.50m, result.TotalAmount);
        Assert.Equal([80m, 95.50m], result.Items.Select(i => i.Amount));
    }

    [Fact]
    public void Compose_UsesSaoPauloMonthBoundaries()
    {
        // 31/10 às 23:59 em Brasília já é 01/11 em UTC, mas é aula de outubro; 01/11 00:00 é de novembro.
        var lastMinute = Approved(Sp(2026, 10, 31, 23, 59));
        var nextMonth = Approved(Sp(2026, 11, 1, 0, 0), respondedAtUtc: Sp(2026, 11, 1, 8));
        var firstMinute = Approved(Sp(2026, 10, 1, 0, 0));
        Assert.Equal(11, lastMinute.OccurredAtUtc.Month);

        var result = BillingClosing.Compose(October, 10, [lastMinute, nextMonth, firstMinute], null)!;

        Assert.Equal([firstMinute.Id, lastMinute.Id], result.Items.Select(i => i.LessonId!.Value));
        Assert.Equal("Aula de 31/10 às 23:59", result.Items[1].Description);
    }

    [Fact]
    public void Compose_IncludesOlderLessonConfirmedLate()
    {
        // Aula de setembro confirmada em 20/10, depois do fechamento de setembro: entra em outubro (D-020, D-043).
        var late = Approved(Sp(2026, 9, 28), respondedAtUtc: Sp(2026, 10, 20));

        var result = BillingClosing.Compose(October, 10, [late], null)!;

        Assert.Equal(late.Id, Assert.Single(result.Items).LessonId);
    }

    [Fact]
    public void Compose_LeavesOutLessonConfirmedAfterTheCutoff()
    {
        // Confirmada em 10/11 às 00:01: depois do corte, fica para o próximo, mesmo que o job rode mais tarde.
        var afterCutoff = Approved(Sp(2026, 10, 28), respondedAtUtc: Sp(2026, 11, 10, 0, 1));
        var beforeCutoff = Approved(Sp(2026, 10, 29), respondedAtUtc: Sp(2026, 11, 9, 23, 59));

        var result = BillingClosing.Compose(October, 10, [afterCutoff, beforeCutoff], null)!;

        Assert.Equal(beforeCutoff.Id, Assert.Single(result.Items).LessonId);
    }

    [Fact]
    public void Compose_LeavesOutPendingRejectedDeletedAndAlreadyBilled()
    {
        var day = Sp(2026, 10, 15);
        ClosingLesson[] lessons =
        [
            new(Guid.NewGuid(), day, 100m, LessonStatus.Pending, null, null),
            new(Guid.NewGuid(), day, 100m, LessonStatus.Rejected, day.AddHours(1), null),
            new(Guid.NewGuid(), day, 100m, LessonStatus.Approved, day.AddHours(1), day.AddHours(2)),
            new(Guid.NewGuid(), day, 100m, LessonStatus.Approved, day.AddHours(1), null, BilledAmount: 100m)
        ];

        Assert.Null(BillingClosing.Compose(October, 10, lessons, null));
    }

    [Fact]
    public void Compose_ReturnsNullForMonthWithoutLessons()
    {
        Assert.Null(BillingClosing.Compose(October, 10, [], null));
    }

    // ---------------------------------------------------------------- Créditos

    [Fact]
    public void Compose_CreditsLessonDeletedAfterBeingBilled()
    {
        // Cobrada em setembro e excluída em 20/10: vira crédito no demonstrativo de outubro (D-045).
        var deleted = new ClosingLesson(Guid.NewGuid(), Sp(2026, 9, 10), 100m, LessonStatus.Approved, Sp(2026, 9, 11),
            Sp(2026, 10, 20), BilledAmount: 90m);
        var charge = Approved(Sp(2026, 10, 3), 100m);

        var result = BillingClosing.Compose(October, 10, [deleted, charge], null)!;

        var credit = Assert.Single(result.Items, i => i.Kind == BillingPeriodItemKind.Adjustment);
        Assert.Equal(deleted.Id, credit.LessonId);
        Assert.Equal(-90m, credit.Amount);
        Assert.Equal("Crédito da aula de 10/09 às 10:00", credit.Description);
        Assert.Equal(10m, result.TotalAmount);
    }

    [Fact]
    public void Compose_DoesNotCreditTwiceOrBeforeTheDeletion()
    {
        var credited = new ClosingLesson(Guid.NewGuid(), Sp(2026, 9, 10), 100m, LessonStatus.Approved, Sp(2026, 9, 11),
            Sp(2026, 10, 20), BilledAmount: 100m, Credited: true);
        var deletedAfterCutoff = new ClosingLesson(Guid.NewGuid(), Sp(2026, 9, 12), 100m, LessonStatus.Approved, Sp(2026, 9, 13),
            Sp(2026, 11, 10, 0, 30), BilledAmount: 100m);

        Assert.Null(BillingClosing.Compose(October, 10, [credited, deletedAfterCutoff], null));
    }

    [Fact]
    public void Compose_CreditThatZeroesTheTotal()
    {
        var deleted = new ClosingLesson(Guid.NewGuid(), Sp(2026, 9, 10), 100m, LessonStatus.Approved, Sp(2026, 9, 11),
            Sp(2026, 10, 2), BilledAmount: 100m);

        var result = BillingClosing.Compose(October, 10, [deleted, Approved(Sp(2026, 10, 3), 100m)], null)!;

        Assert.True(result.IsZero);
        Assert.Equal(0m, result.CarryOut);
    }

    [Fact]
    public void Compose_CreditOnlyMonthIsIssuedWithZeroTotalAndCarriesTheBalance()
    {
        // Sem aula em outubro e com crédito: emite com total zero e leva o saldo (D-044).
        var deleted = new ClosingLesson(Guid.NewGuid(), Sp(2026, 9, 10), 120m, LessonStatus.Approved, Sp(2026, 9, 11),
            Sp(2026, 10, 2), BilledAmount: 120m);

        var result = BillingClosing.Compose(October, 10, [deleted], null)!;

        Assert.True(result.IsZero);
        Assert.Equal(0, result.LessonCount);
        Assert.Equal(120m, result.CarryOut);
    }

    [Fact]
    public void Compose_CarriesTheBalanceToTheNextStatement()
    {
        var deletedA = new ClosingLesson(Guid.NewGuid(), Sp(2026, 9, 10), 100m, LessonStatus.Approved, Sp(2026, 9, 11),
            Sp(2026, 10, 2), BilledAmount: 100m);
        var deletedB = new ClosingLesson(Guid.NewGuid(), Sp(2026, 9, 17), 100m, LessonStatus.Approved, Sp(2026, 9, 18),
            Sp(2026, 10, 2), BilledAmount: 100m);

        var october = BillingClosing.Compose(October, 10, [deletedA, deletedB, Approved(Sp(2026, 10, 3), 50m)], null)!;
        Assert.Equal(0m, october.TotalAmount);
        Assert.Equal(150m, october.CarryOut);

        var november = BillingClosing.Compose(new DateOnly(2026, 11, 1), 10,
            [Approved(Sp(2026, 11, 3), 100m), Approved(Sp(2026, 11, 5), 100m)], new CarriedCredit(October, october.CarryOut))!;

        var carry = Assert.Single(november.Items, i => i is { Kind: BillingPeriodItemKind.Adjustment, LessonId: null });
        Assert.Equal(-150m, carry.Amount);
        Assert.Equal("Saldo de crédito de outubro de 2026", carry.Description);
        Assert.Equal(50m, november.TotalAmount);
        Assert.Equal(0m, november.CarryOut);
    }

    [Fact]
    public void Compose_CarryAloneDoesNotIssueAStatement()
    {
        // Só o saldo, sem aula nem crédito novo: não emite, e o saldo espera o próximo demonstrativo.
        Assert.Null(BillingClosing.Compose(October, 10, [], new CarriedCredit(new DateOnly(2026, 9, 1), 50m)));
    }

    [Fact]
    public void CarryOutOf_IsTheNegativePartOfTheItemsSum()
    {
        Assert.Equal(0m, BillingClosing.CarryOutOf(10m));
        Assert.Equal(0m, BillingClosing.CarryOutOf(0m));
        Assert.Equal(30m, BillingClosing.CarryOutOf(-30m));
    }

    // ---------------------------------------------------------------- Prévia

    [Fact]
    public void Preview_IncludesLessonsConfirmedUpToNow()
    {
        var now = Sp(2026, 11, 5, 12);
        var confirmedNow = Approved(Sp(2026, 11, 4), respondedAtUtc: now);

        var preview = BillingClosing.Preview(new DateOnly(2026, 11, 1), 10, [confirmedNow], null, now)!;

        Assert.Equal(1, preview.LessonCount);
        Assert.Equal(new DateOnly(2026, 12, 10), preview.DueDate);
    }

    [Fact]
    public void Preview_AfterTheCutoffMatchesTheClosing()
    {
        var lessons = new[] { Approved(Sp(2026, 10, 28), respondedAtUtc: Sp(2026, 11, 10, 9)), Approved(Sp(2026, 10, 3)) };

        var preview = BillingClosing.Preview(October, 10, lessons, null, Sp(2026, 11, 12))!;
        var closing = BillingClosing.Compose(October, 10, lessons, null)!;

        Assert.Equal(closing.Items, preview.Items);
    }
}
