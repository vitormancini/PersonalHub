using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Lessons;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Features.Billing.CloseBilling;
using PersonalHub.Web.Features.Lessons.DeleteLesson;
using PersonalHub.Web.Infrastructure.Identity;
using static PersonalHub.IntegrationTests.Billing.BillingTestHelpers;

namespace PersonalHub.IntegrationTests.Billing;

/// <summary>
/// Job de fechamento contra o PostgreSQL real: emissão, idempotência, corridas, recuperação de dias perdidos e
/// créditos. As datas ficam em 2031 para não depender do dia em que a suíte roda.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BillingClosingJobTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly February = new(2031, 2, 1);
    private static readonly DateOnly March = new(2031, 3, 1);
    private static readonly DateOnly April = new(2031, 4, 1);

    private WebApplicationFactory<Program> _factory = default!;
    private TestAccount _personal = default!;
    private Guid _linkId;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _personal = await _factory.CreateAccountAsync(AppRoles.Personal);
        _linkId = (await _factory.CreateInviteAsync(_personal.Id)).PersonalStudentId;
        await _factory.LinkStudentAsync(_linkId);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    // ---------------------------------------------------------------- Emissão

    [Fact]
    public async Task Run_OnDueDate_IssuesStatementWithFrozenItems()
    {
        var first = await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3, 7));
        await _factory.SetBillingAsync(_linkId, price: 95.50m);
        var second = await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 28, 23, 59));
        await _factory.AddLessonAsync(_linkId, Sp(2031, 2, 10));                                   // pendente
        await _factory.AddLessonAsync(_linkId, Sp(2031, 2, 11), LessonStatus.Rejected);
        await _factory.AddLessonAsync(_linkId, Sp(2031, 2, 12), LessonStatus.Approved, deleted: true);
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 3, 1, 0, 0));                     // já é março
        var now = SpClock(2031, 3, 10, 0, 30);

        var run = await _factory.RunClosingAsync(now);

        Assert.True(run.Ran);
        var period = Assert.Single(await _factory.PeriodsOfAsync(_linkId));
        Assert.Equal(February, period.ReferenceMonth);
        Assert.Equal(February, period.PeriodStart);
        Assert.Equal(March, period.PeriodEnd);
        Assert.Equal(new DateOnly(2031, 3, 10), period.DueDate);
        Assert.Equal(175.50m, period.TotalAmount);
        Assert.Equal(BillingPeriodStatus.Issued, period.Status);
        Assert.Null(period.PaidAtUtc);
        Assert.Equal(now.UtcDateTime, period.IssuedAtUtc, TimeSpan.FromMilliseconds(1));

        var items = await _factory.ItemsOfAsync(period.Id);
        Assert.All(items, i => Assert.Equal(BillingPeriodItemKind.Lesson, i.Kind));
        Assert.Equal(
            [("Aula de 03/02 às 07:00", 80m, first.Id), ("Aula de 28/02 às 23:59", 95.50m, second.Id)],
            items.Select(i => (i.Description, i.Amount, i.LessonId!.Value)));

        var audit = await _factory.WithDbAsync(db => db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == period.Id));
        Assert.Equal("BillingPeriodIssued", audit.Action);
        Assert.Null(audit.ActorUserId);
        using var metadata = JsonDocument.Parse(audit.Metadata!);
        Assert.Equal("2031-02", metadata.RootElement.GetProperty("referenceMonth").GetString());
        Assert.Equal(2, metadata.RootElement.GetProperty("lessons").GetInt32());
        Assert.DoesNotContain("Mariana", audit.Metadata);
    }

    [Fact]
    public async Task Run_BeforeTheDueDate_DoesNotClose()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));

        await _factory.RunClosingAsync(SpClock(2031, 3, 9, 23, 59));

        Assert.Empty(await _factory.PeriodsOfAsync(_linkId));
    }

    [Fact]
    public async Task Run_ShortMonth_ClosesOnTheLastDay()
    {
        await _factory.SetBillingAsync(_linkId, dueDay: 31);
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 1, 20));

        await _factory.RunClosingAsync(SpClock(2031, 2, 27, 23));
        Assert.Empty(await _factory.PeriodsOfAsync(_linkId));

        await _factory.RunClosingAsync(SpClock(2031, 2, 28, 0, 5));
        var period = Assert.Single(await _factory.PeriodsOfAsync(_linkId));
        Assert.Equal(new DateOnly(2031, 1, 1), period.ReferenceMonth);
        Assert.Equal(new DateOnly(2031, 2, 28), period.DueDate);
    }

    [Fact]
    public async Task Run_MonthWithoutChargeableLessons_IssuesNothing()
    {
        await _factory.AddLessonAsync(_linkId, Sp(2031, 2, 3));                                     // só pendente

        var run = await _factory.RunClosingAsync(SpClock(2031, 3, 10));

        Assert.True(run.Empty >= 1);
        Assert.Empty(await _factory.PeriodsOfAsync(_linkId));
    }

    [Fact]
    public async Task Run_InactiveLink_StillClosesWhatIsPending()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));
        await _factory.SetInactiveAsync(_linkId, Sp(2031, 2, 20));

        await _factory.RunClosingAsync(SpClock(2031, 3, 10));
        await _factory.RunClosingAsync(SpClock(2031, 4, 10));

        var period = Assert.Single(await _factory.PeriodsOfAsync(_linkId));
        Assert.Equal(February, period.ReferenceMonth);
    }

    // ---------------------------------------------------------------- Idempotência e corridas

    [Fact]
    public async Task Run_Twice_IssuesOnce()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));

        await _factory.RunClosingAsync(SpClock(2031, 3, 10, 1));
        await _factory.RunClosingAsync(SpClock(2031, 3, 10, 2));

        var period = Assert.Single(await _factory.PeriodsOfAsync(_linkId));
        Assert.Single(await _factory.ItemsOfAsync(period.Id));
    }

    [Fact]
    public async Task Run_InParallel_IssuesOnce()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 4));
        var now = SpClock(2031, 3, 10);

        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _factory.RunClosingAsync(now)));

        Assert.Contains(runs, r => r.Ran);
        Assert.All(runs, r => Assert.Equal(0, r.Failed));
        var period = Assert.Single(await _factory.PeriodsOfAsync(_linkId));
        Assert.Equal(2, (await _factory.ItemsOfAsync(period.Id)).Count);
    }

    [Fact]
    public async Task CloseLink_InParallel_SecondSeesTheFirst()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));
        var now = SpClock(2031, 3, 10);

        var outcomes = await Task.WhenAll(
            _factory.CloseAsync(_linkId, February, now),
            _factory.CloseAsync(_linkId, February, now));

        Assert.Equal([CloseLinkOutcome.Issued, CloseLinkOutcome.AlreadyClosed], outcomes.Order());
        Assert.Single(await _factory.PeriodsOfAsync(_linkId));
    }

    [Fact]
    public async Task Run_WhileAnotherInstanceHoldsTheLock_Skips()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));
        await using var other = new NpgsqlConnection(fixture.ConnectionString);
        await other.OpenAsync();
        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_lock({BillingClosingJob.AdvisoryLockKey})", other))
        {
            await command.ExecuteScalarAsync();
        }

        var skipped = await _factory.RunClosingAsync(SpClock(2031, 3, 10));
        Assert.False(skipped.Ran);
        Assert.Empty(await _factory.PeriodsOfAsync(_linkId));

        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_unlock({BillingClosingJob.AdvisoryLockKey})", other))
        {
            await command.ExecuteScalarAsync();
        }

        Assert.True((await _factory.RunClosingAsync(SpClock(2031, 3, 10))).Ran);
        Assert.Single(await _factory.PeriodsOfAsync(_linkId));
    }

    [Fact]
    public async Task Database_RejectsChargingTheSameLessonTwice()
    {
        var lesson = await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));
        await _factory.CloseAsync(_linkId, February, SpClock(2031, 3, 10));
        var period = Assert.Single(await _factory.PeriodsOfAsync(_linkId));

        await DbAssert.ViolatesAsync(PostgresErrorCodes.UniqueViolation, "IX_BillingPeriodItem_LessonId_Kind", () =>
            _factory.WithDbAsync(db =>
            {
                db.BillingPeriodItems.Add(new BillingPeriodItem
                {
                    Id = Guid.NewGuid(), BillingPeriodId = period.Id, Kind = BillingPeriodItemKind.Lesson, LessonId = lesson.Id,
                    Description = "Aula", Amount = 80m, CreatedAtUtc = DateTime.UtcNow
                });
                return db.SaveChangesAsync();
            }));
    }

    // ---------------------------------------------------------------- Dias perdidos e confirmação com atraso

    [Fact]
    public async Task Run_AfterDowntime_ClosesTheLatestMonthWithEverythingPending()
    {
        // O app ficou fora de 09/03 a 20/05: o fechamento de abril leva também fevereiro e março.
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 3, 3));
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 4, 3));

        await _factory.RunClosingAsync(SpClock(2031, 5, 20, 14));

        var period = Assert.Single(await _factory.PeriodsOfAsync(_linkId));
        Assert.Equal(April, period.ReferenceMonth);
        Assert.Equal(new DateOnly(2031, 5, 10), period.DueDate);
        Assert.Equal(3, (await _factory.ItemsOfAsync(period.Id)).Count);

        await _factory.RunClosingAsync(SpClock(2031, 6, 10));
        Assert.Single(await _factory.PeriodsOfAsync(_linkId));
    }

    [Fact]
    public async Task Run_LessonConfirmedAfterTheCutoff_GoesToTheNextStatement()
    {
        var onTime = await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));
        var late = await _factory.AddLessonAsync(_linkId, Sp(2031, 2, 26));

        // Confirmada em 10/03 às 08:00, mas o job só roda às 09:00: o corte é 00:00, então fica para março.
        await _factory.ApproveAtAsync(late.Id, Sp(2031, 3, 10, 8));
        await _factory.RunClosingAsync(SpClock(2031, 3, 10, 9));
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 3, 5));
        await _factory.RunClosingAsync(SpClock(2031, 4, 10));

        var periods = await _factory.PeriodsOfAsync(_linkId);
        Assert.Equal([February, March], periods.Select(p => p.ReferenceMonth));
        Assert.Equal([onTime.Id], (await _factory.ItemsOfAsync(periods[0].Id)).Select(i => i.LessonId!.Value));
        Assert.Contains(late.Id, (await _factory.ItemsOfAsync(periods[1].Id)).Select(i => i.LessonId!.Value));
    }

    // ---------------------------------------------------------------- Créditos

    [Fact]
    public async Task DeletingABilledLesson_CreditsTheNextStatement_AndKeepsTheIssuedOne()
    {
        var billed = await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 4));
        await _factory.RunClosingAsync(SpClock(2031, 3, 10));

        Assert.Equal(DeleteLessonOutcome.Succeeded, await _factory.DeleteLessonAtAsync(_personal.Id, billed.Id, SpClock(2031, 3, 15)));
        await _factory.SetBillingAsync(_linkId, price: 100m);
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 3, 20));
        await _factory.RunClosingAsync(SpClock(2031, 4, 10));

        var periods = await _factory.PeriodsOfAsync(_linkId);
        Assert.Equal(160m, periods[0].TotalAmount);
        Assert.Equal(2, (await _factory.ItemsOfAsync(periods[0].Id)).Count);

        Assert.Equal(20m, periods[1].TotalAmount);
        var credit = Assert.Single(await _factory.ItemsOfAsync(periods[1].Id), i => i.Kind == BillingPeriodItemKind.Adjustment);
        Assert.Equal(billed.Id, credit.LessonId);
        Assert.Equal(-80m, credit.Amount);

        // Não credita de novo no mês seguinte.
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 4, 2));
        await _factory.RunClosingAsync(SpClock(2031, 5, 10));
        var may = (await _factory.PeriodsOfAsync(_linkId))[2];
        Assert.All(await _factory.ItemsOfAsync(may.Id), i => Assert.Equal(BillingPeriodItemKind.Lesson, i.Kind));
    }

    [Fact]
    public async Task DeletingAPendingLesson_DoesNotCredit()
    {
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));
        var pending = await _factory.AddLessonAsync(_linkId, Sp(2031, 2, 4));
        await _factory.RunClosingAsync(SpClock(2031, 3, 10));
        await _factory.DeleteLessonAtAsync(_personal.Id, pending.Id, SpClock(2031, 3, 15));

        await _factory.RunClosingAsync(SpClock(2031, 4, 10));

        Assert.Single(await _factory.PeriodsOfAsync(_linkId));
    }

    [Fact]
    public async Task CreditLargerThanTheTotal_IssuesZeroAsPaid_AndCarriesTheBalance()
    {
        var billed = await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 2, 3));
        await _factory.RunClosingAsync(SpClock(2031, 3, 10));
        await _factory.DeleteLessonAtAsync(_personal.Id, billed.Id, SpClock(2031, 3, 15));

        // Março só tem o crédito: emite com total zero, já pago, e leva R$ 80 adiante (D-044).
        var marchClock = SpClock(2031, 4, 10);
        await _factory.RunClosingAsync(marchClock);
        var march = (await _factory.PeriodsOfAsync(_linkId))[1];
        Assert.Equal(0m, march.TotalAmount);
        Assert.Equal(BillingPeriodStatus.Paid, march.Status);
        Assert.Equal(marchClock.UtcDateTime, march.PaidAtUtc!.Value, TimeSpan.FromMilliseconds(1));

        await _factory.SetBillingAsync(_linkId, price: 100m);
        await _factory.AddApprovedLessonAsync(_linkId, Sp(2031, 4, 2));
        await _factory.RunClosingAsync(SpClock(2031, 5, 10));

        var april = (await _factory.PeriodsOfAsync(_linkId))[2];
        Assert.Equal(20m, april.TotalAmount);
        Assert.Equal(BillingPeriodStatus.Issued, april.Status);
        var carry = Assert.Single(await _factory.ItemsOfAsync(april.Id), i => i.Kind == BillingPeriodItemKind.Adjustment);
        Assert.Null(carry.LessonId);
        Assert.Equal(-80m, carry.Amount);
        Assert.Equal("Saldo de crédito de março de 2031", carry.Description);
    }
}
