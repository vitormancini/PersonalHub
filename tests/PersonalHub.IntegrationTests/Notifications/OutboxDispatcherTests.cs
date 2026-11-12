using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Features.Notifications;
using PersonalHub.Web.Infrastructure.Persistence;
using PersonalHub.Web.Infrastructure.WhatsApp;
using static PersonalHub.IntegrationTests.Billing.BillingTestHelpers;

namespace PersonalHub.IntegrationTests.Notifications;

/// <summary>
/// Outbox e worker (ARCHITECTURE §6, D-061, D-062): idempotência, transação, backoff, silêncio, SKIP LOCKED, lease e
/// limpeza do código. Relógio parado em 12/11/2026 14:00 de Brasília.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OutboxDispatcherTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly FakeWhatsAppSender _sender = new();
    private readonly MutableClock _clock = new(SpClock(2026, 11, 12, 14));
    private WebApplicationFactory<Program> _factory = default!;

    private DateTime Now => _clock.Now.UtcDateTime;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString).WithWhatsApp(_sender, _clock);
        await _factory.QuiesceOutboxAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Enqueue_SameKeyTwice_KeepsOneRow()
    {
        var key = $"lesson-created:{Guid.NewGuid()}";
        var request = new NotificationRequest("ph_nova_aula", key, null, "+5511987652468",
            new NotificationPayload(["a"], "aluno/aulas"));

        var results = await _factory.WithDbAsync(async db =>
        {
            var first = await db.EnqueueNotificationAsync(request, Now, CancellationToken.None);
            var sameContext = await db.EnqueueNotificationAsync(request, Now, CancellationToken.None);
            await db.SaveChangesAsync();
            return (first, sameContext);
        });
        var later = await _factory.WithDbAsync(async db =>
        {
            var added = await db.EnqueueNotificationAsync(request, Now, CancellationToken.None);
            await db.SaveChangesAsync();
            return added;
        });

        Assert.True(results.first);
        Assert.False(results.sameContext);
        Assert.False(later);
        Assert.Equal(1, await _factory.WithDbAsync(db => db.NotificationOutbox.CountAsync(o => o.IdempotencyKey == key)));
    }

    [Fact]
    public async Task UniqueIndex_RejectsDuplicateKey_EvenPastTheCheck()
    {
        var key = $"lesson-created:{Guid.NewGuid()}";
        await _factory.WithDbAsync(db => Insert(db, key));

        await DbAssert.ViolatesAsync(Npgsql.PostgresErrorCodes.UniqueViolation, "IX_NotificationOutbox_IdempotencyKey",
            () => _factory.WithDbAsync(db => Insert(db, key)));
    }

    private Task<int> Insert(AppDbContext db, string key)
    {
        db.NotificationOutbox.Add(new NotificationOutbox
        {
            Id = Guid.NewGuid(), EventType = "ph_nova_aula", Payload = "{}", IdempotencyKey = key,
            NextAttemptAtUtc = Now, CreatedAtUtc = Now
        });
        return db.SaveChangesAsync();
    }

    [Fact]
    public async Task Enqueue_InsideRolledBackTransaction_LeavesNoRow()
    {
        var key = $"lesson-created:{Guid.NewGuid()}";

        await _factory.WithDbAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.EnqueueNotificationAsync(new NotificationRequest("ph_nova_aula", key, null, "+5511987652468",
                new NotificationPayload(["a"], "aluno/aulas")), Now, CancellationToken.None);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        });

        Assert.Null(await _factory.OutboxByKeyAsync(key));
    }

    [Fact]
    public async Task Dispatch_Sends_AndRecordsProviderMessageId()
    {
        var row = await _factory.EnqueueAsync(Now);

        var result = await _factory.DispatchAsync();

        var saved = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(1, result.Sent);
        Assert.Equal(NotificationStatus.Sent, saved.Status);
        Assert.Equal(Now, saved.SentAtUtc);
        Assert.StartsWith("wamid.", saved.ProviderMessageId);
        Assert.Equal(1, saved.Attempts);
        Assert.Null(saved.LeaseExpiresAtUtc);
        var message = Assert.Single(_sender.Sent);
        Assert.Equal("+5511987652468", message.ToE164);
        Assert.Equal("ph_nova_aula", message.Template);
        Assert.Equal(["Mariana", "Carlos Lima", "12/11", "07:30"], message.BodyParameters);
        Assert.Equal("aluno/aulas/x", message.ButtonUrlSuffix);

        // Já enviado: a próxima rodada não manda de novo.
        await _factory.DispatchAsync();
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task Dispatch_Retryable_BacksOff_1m_5m_30m_2h_ThenFails()
    {
        _sender.Respond = _ => WhatsAppSendResult.Retry("http:503");
        var row = await _factory.EnqueueAsync(Now);
        TimeSpan[] waits = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2)];

        foreach (var wait in waits)
        {
            await _factory.DispatchAsync();
            var pending = await _factory.OutboxRowAsync(row.Id);
            Assert.Equal(NotificationStatus.Pending, pending.Status);
            Assert.Equal(Now + wait, pending.NextAttemptAtUtc);
            Assert.Equal("http:503", pending.LastError);

            // Antes da hora não tenta.
            _clock.Advance(wait - TimeSpan.FromSeconds(1));
            Assert.Equal(0, (await _factory.DispatchAsync()).Claimed);
            _clock.Advance(TimeSpan.FromSeconds(1));
        }

        await _factory.DispatchAsync();

        var failed = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationStatus.Failed, failed.Status);
        Assert.Equal(5, failed.Attempts);
        Assert.Equal(5, _sender.Sent.Count);
        var audit = await _factory.WithDbAsync(db => db.AuditLogs.SingleAsync(a => a.EntityId == row.Id));
        Assert.Equal("NotificationFailed", audit.Action);
        Assert.Contains("http:503", audit.Metadata);
        Assert.DoesNotContain("5511987652468", audit.Metadata);
    }

    [Fact]
    public async Task Dispatch_RetryLandingInQuietHours_MovesTo8am()
    {
        _clock.Now = SpClock(2026, 11, 12, 20, 0);
        _sender.Respond = _ => WhatsAppSendResult.Retry("http:503");
        var row = await _factory.EnqueueAsync(Now);
        await _factory.DispatchAsync(); // +1 min
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _factory.DispatchAsync(); // +5 min
        _clock.Now = SpClock(2026, 11, 12, 21, 50);
        await _factory.DispatchAsync(); // +30 min cairia às 22:20

        Assert.Equal(Sp(2026, 11, 13, 8), (await _factory.OutboxRowAsync(row.Id)).NextAttemptAtUtc);
    }

    [Fact]
    public async Task Dispatch_PermanentError_FailsAtOnce()
    {
        _sender.Respond = _ => WhatsAppSendResult.Fail("meta:131026");
        var row = await _factory.EnqueueAsync(Now);

        await _factory.DispatchAsync();

        var saved = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationStatus.Failed, saved.Status);
        Assert.Equal("meta:131026", saved.LastError);
        Assert.Equal(1, saved.Attempts);
    }

    [Fact]
    public async Task Dispatch_AdapterThrows_IsRetried()
    {
        _sender.Respond = _ => throw new InvalidOperationException("falha +5511987652468");
        var row = await _factory.EnqueueAsync(Now);

        await _factory.DispatchAsync();

        var saved = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationStatus.Pending, saved.Status);
        Assert.Equal("erro-interno", saved.LastError);
    }

    [Fact]
    public async Task Dispatch_WithoutPhone_FailsAsSemCelular_WithoutSending()
    {
        var row = await _factory.EnqueueAsync(Now, phone: null);

        await _factory.DispatchAsync();

        var saved = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationStatus.Failed, saved.Status);
        Assert.Equal("sem-celular", saved.LastError);
        Assert.Empty(_sender.Sent);
    }

    [Theory]
    [InlineData(22, 30, 13)]
    [InlineData(23, 59, 13)]
    [InlineData(6, 0, 12)]
    public async Task Enqueue_InQuietHours_IsScheduledFor8am(int hour, int minute, int expectedDay)
    {
        var at = Sp(2026, 11, 12, hour, minute);
        var row = await _factory.EnqueueAsync(at);

        Assert.Equal(Sp(2026, 11, expectedDay, 8), row.NextAttemptAtUtc);

        _clock.Now = new DateTimeOffset(at);
        Assert.Equal(0, (await _factory.DispatchAsync()).Claimed);
        _clock.Now = new DateTimeOffset(Sp(2026, 11, expectedDay, 8));
        Assert.Equal(1, (await _factory.DispatchAsync()).Sent);
    }

    [Fact]
    public async Task Enqueue_Code_InQuietHours_GoesOutImmediately()
    {
        _clock.Now = SpClock(2026, 11, 12, 23, 30);
        var row = await _factory.EnqueueAsync(Now, isAuthentication: true, code: "123456", expiresAtUtc: Now.AddMinutes(10),
            template: NotificationTemplates.Code);

        Assert.Equal(Now, row.NextAttemptAtUtc);
        Assert.Equal(1, (await _factory.DispatchAsync()).Sent);
        Assert.Equal("123456", Assert.Single(_sender.Sent).Code);
    }

    [Fact]
    public async Task Code_PayloadNeverHasPlainCode_AndIsClearedAfterSend()
    {
        var row = await _factory.EnqueueAsync(Now, isAuthentication: true, code: "654321", expiresAtUtc: Now.AddMinutes(10),
            template: NotificationTemplates.Code);
        Assert.DoesNotContain("654321", row.Payload);

        await _factory.DispatchAsync();

        var saved = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationStatus.Sent, saved.Status);
        Assert.Equal("{}", saved.Payload);
    }

    [Fact]
    public async Task Code_PayloadIsClearedAfterFinalFailure_AndNotRetriedPastExpiry()
    {
        _sender.Respond = _ => WhatsAppSendResult.Retry("http:503");
        var row = await _factory.EnqueueAsync(Now, isAuthentication: true, code: "654321", expiresAtUtc: Now.AddMinutes(10),
            template: NotificationTemplates.Code);

        await _factory.DispatchAsync();                  // falha; próxima em 1 min
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _factory.DispatchAsync();                  // falha; próxima em 5 min (ainda dentro dos 10)
        _clock.Advance(TimeSpan.FromMinutes(5));
        await _factory.DispatchAsync();                  // falha; +30 min passaria do vencimento: desiste

        var saved = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationStatus.Failed, saved.Status);
        Assert.Equal(3, saved.Attempts);
        Assert.Equal("{}", saved.Payload);
    }

    [Fact]
    public async Task Code_Expired_IsNotSent()
    {
        var row = await _factory.EnqueueAsync(Now, isAuthentication: true, code: "654321", expiresAtUtc: Now.AddMinutes(10),
            template: NotificationTemplates.Code);
        _clock.Advance(TimeSpan.FromMinutes(11));

        await _factory.DispatchAsync();

        var saved = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationStatus.Failed, saved.Status);
        Assert.Equal("expirado", saved.LastError);
        Assert.Equal("{}", saved.Payload);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Claim_SkipsRowsLockedByAnotherTransaction()
    {
        var locked = await _factory.EnqueueAsync(Now);
        var free = await _factory.EnqueueAsync(Now);

        await using var scope = _factory.Services.CreateAsyncScope();
        var other = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await other.Database.BeginTransactionAsync();
        await other.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM "NotificationOutbox" WHERE "Id" = {locked.Id} FOR UPDATE""");

        await _factory.DispatchAsync();

        Assert.Equal(NotificationStatus.Pending, (await _factory.OutboxRowAsync(locked.Id)).Status);
        Assert.Equal(NotificationStatus.Sent, (await _factory.OutboxRowAsync(free.Id)).Status);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task TwoWorkersInParallel_NeverSendTheSameRowTwice()
    {
        var rows = new List<NotificationOutbox>();
        for (var i = 0; i < 12; i++)
        {
            rows.Add(await _factory.EnqueueAsync(Now));
        }

        _sender.Delay = TimeSpan.FromMilliseconds(20);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => _factory.DispatchAsync()));

        Assert.Equal(12, results.Sum(r => r.Sent));
        Assert.Equal(12, _sender.Sent.Count);
        var ids = await _factory.WithDbAsync(db => db.NotificationOutbox.AsNoTracking()
            .Where(o => rows.Select(r => r.Id).Contains(o.Id)).ToListAsync());
        Assert.All(ids, o => Assert.Equal((NotificationStatus.Sent, 1), (o.Status, o.Attempts)));
    }

    [Fact]
    public async Task ExpiredLease_IsReclaimed_AndCountsTheAttempt()
    {
        var row = await _factory.EnqueueAsync(Now);
        await _factory.WithDbAsync(async db =>
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var claimed = await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().ClaimAsync(CancellationToken.None);
            Assert.Contains(claimed, c => c.Id == row.Id);
            return 0;
        });
        // O processo "caiu" depois de pegar a linha: ela fica em Processing até o prazo vencer.
        Assert.Equal(NotificationStatus.Processing, (await _factory.OutboxRowAsync(row.Id)).Status);
        Assert.Equal(0, (await _factory.DispatchAsync()).Claimed);

        _clock.Advance(TimeSpan.FromMinutes(2));
        await _factory.DispatchAsync();

        var saved = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationStatus.Sent, saved.Status);
        Assert.Equal(2, saved.Attempts);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task ExpiredLease_OnTheLastAttempt_FailsWithoutSending()
    {
        var row = await _factory.EnqueueAsync(Now);
        await _factory.WithDbAsync(db => db.NotificationOutbox.Where(o => o.Id == row.Id).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, NotificationStatus.Processing)
            .SetProperty(o => o.Attempts, NotificationSchedule.MaxAttempts)
            .SetProperty(o => o.LeaseExpiresAtUtc, Now.AddSeconds(-1))));

        await _factory.DispatchAsync();

        var saved = await _factory.OutboxRowAsync(row.Id);
        Assert.Equal(NotificationStatus.Failed, saved.Status);
        Assert.Equal("tentativas", saved.LastError);
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task LogSender_MasksThePhone()
    {
        var logs = new Observability.CapturingLoggerProvider();
        await using var factory = TestApp.Create(fixture.ConnectionString, logs: logs);
        var sender = factory.Services.GetRequiredService<LogWhatsAppSender>();

        await sender.SendTemplateAsync(new WhatsAppTemplateMessage("+5511987652468", "ph_nova_aula", ["Mariana"], "aluno/aulas"),
            CancellationToken.None);

        var log = Assert.Single(logs.Logs, l => l.Message.Contains("ph_nova_aula"));
        Assert.DoesNotContain("987652468", log.Message);
        Assert.Contains("2468", log.Message);
        Assert.Contains("https://localhost/aluno/aulas", log.Message);
    }
}
