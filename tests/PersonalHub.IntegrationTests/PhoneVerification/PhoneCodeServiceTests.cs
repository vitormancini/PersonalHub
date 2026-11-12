using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Authentication;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.IntegrationTests.Notifications;
using PersonalHub.IntegrationTests.Students;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Domain.PhoneVerification;
using PersonalHub.Web.Features.PhoneVerification;
using PersonalHub.Web.Infrastructure.Identity;
using static PersonalHub.IntegrationTests.Billing.BillingTestHelpers;

namespace PersonalHub.IntegrationTests.PhoneVerification;

/// <summary>Regras dos códigos (D-056) contra o banco: validade, tentativas, reenvio, limite por hora e bloqueio.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class PhoneCodeServiceTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly MutableClock _clock = new(SpClock(2026, 11, 12, 14));
    private WebApplicationFactory<Program> _factory = default!;
    private PhoneCodeScope _scope = default!;

    public async Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString).WithWhatsApp(new FakeWhatsAppSender(), _clock);
        var user = await _factory.CreateAccountAsync(AppRoles.Student);
        _scope = new PhoneCodeScope(PhoneCodePurpose.PasswordReset, "+55" + StudentTestHelpers.RandomPhone(), UserId: user.Id);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<T> WithServiceAsync<T>(Func<PhoneCodeService, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<PhoneCodeService>());
    }

    private Task<PhoneCodeSendResult> SendAsync() => WithServiceAsync(s => s.SendAsync(_scope, _scope.UserId, "teste", CancellationToken.None));

    private Task<PhoneCodeVerifyResult> VerifyAsync(string? code) =>
        WithServiceAsync(s => s.VerifyAsync(_scope, code, "teste", CancellationToken.None));

    private Task<string> CodeAsync() => _factory.LatestCodeAsync(_scope.Purpose, _scope.UserId!.Value);

    private static string Wrong(string code) => ((int.Parse(code) + 1) % 1_000_000).ToString("D6");

    [Fact]
    public async Task Send_StoresOnlyTheHmac_AndQueuesAnAuthenticationMessage()
    {
        Assert.Equal(PhoneCodeSendOutcome.Sent, (await SendAsync()).Outcome);

        var code = await CodeAsync();
        var row = await _factory.WithDbAsync(db => db.PhoneVerificationCodes.AsNoTracking().SingleAsync(c => c.UserId == _scope.UserId));
        Assert.Equal(6, code.Length);
        Assert.Equal(32, row.CodeHash.Length);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(10), row.ExpiresAtUtc);

        var outbox = (await _factory.OutboxByKeyAsync($"phone-code:{row.Id}"))!;
        Assert.Equal(NotificationTemplates.Code, outbox.EventType);
        Assert.True(outbox.IsAuthentication);
        Assert.Equal(_scope.Phone, outbox.RecipientPhone);
        Assert.Equal(row.ExpiresAtUtc, outbox.ExpiresAtUtc);
        Assert.DoesNotContain(code, outbox.Payload);

        var audit = await _factory.WithDbAsync(db => db.AuditLogs.SingleAsync(a => a.EntityId == row.Id));
        Assert.Equal("PhoneCodeSent", audit.Action);
        Assert.DoesNotContain(_scope.Phone[3..], audit.Metadata);
        Assert.DoesNotContain(code, audit.Metadata);
    }

    [Fact]
    public async Task Verify_RightCode_Once()
    {
        await SendAsync();
        var code = await CodeAsync();

        Assert.Equal(PhoneCodeVerifyOutcome.Verified, (await VerifyAsync(code)).Outcome);
        Assert.Equal(PhoneCodeVerifyOutcome.Expired, (await VerifyAsync(code)).Outcome);
        Assert.Contains("PhoneCodeVerified", await _factory.WithDbAsync(db =>
            db.AuditLogs.Where(a => a.ActorUserId == _scope.UserId).Select(a => a.Action).ToListAsync()));
    }

    [Fact]
    public async Task Verify_AfterTenMinutes_IsExpired()
    {
        await SendAsync();
        var code = await CodeAsync();
        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(PhoneCodeVerifyOutcome.Expired, (await VerifyAsync(code)).Outcome);
    }

    [Fact]
    public async Task Verify_EmptyOrMalformed_DoesNotCountAttempt()
    {
        await SendAsync();

        Assert.Equal(PhoneCodeVerifyOutcome.Invalid, (await VerifyAsync("")).Outcome);
        Assert.Equal(PhoneCodeVerifyOutcome.Invalid, (await VerifyAsync("12ab")).Outcome);
        Assert.Equal(0, await _factory.WithDbAsync(db =>
            db.PhoneVerificationCodes.Where(c => c.UserId == _scope.UserId).Select(c => c.Attempts).SingleAsync()));
    }

    [Fact]
    public async Task Verify_FiveWrongAttempts_LockFor15Minutes_EvenWithTheRightCode()
    {
        await SendAsync();
        var code = await CodeAsync();

        for (var remaining = 4; remaining >= 1; remaining--)
        {
            var result = await VerifyAsync(Wrong(code));
            Assert.Equal((PhoneCodeVerifyOutcome.Wrong, remaining), (result.Outcome, result.RemainingAttempts));
        }

        var fifth = await VerifyAsync(Wrong(code));
        Assert.Equal(PhoneCodeVerifyOutcome.Locked, fifth.Outcome);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(15), fifth.LockedUntilUtc);

        Assert.Equal(PhoneCodeVerifyOutcome.Locked, (await VerifyAsync(code)).Outcome);
        Assert.Equal(PhoneCodeSendOutcome.Locked, (await SendAsync()).Outcome);

        _clock.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(PhoneCodeSendOutcome.Sent, (await SendAsync()).Outcome);
        Assert.Equal(PhoneCodeVerifyOutcome.Verified, (await VerifyAsync(await CodeAsync())).Outcome);

        var actions = await _factory.WithDbAsync(db =>
            db.AuditLogs.Where(a => a.ActorUserId == _scope.UserId).Select(a => a.Action).ToListAsync());
        Assert.Equal(4, actions.Count(a => a == "PhoneCodeFailed"));
        Assert.Contains("PhoneCodeLocked", actions);
    }

    [Fact]
    public async Task Resend_Before60Seconds_IsRefused_AndAfterReplacesTheOldCode()
    {
        await SendAsync();
        var first = await CodeAsync();

        _clock.Advance(TimeSpan.FromSeconds(59));
        var early = await SendAsync();
        Assert.Equal(PhoneCodeSendOutcome.Cooldown, early.Outcome);
        Assert.Equal(_clock.Now.UtcDateTime.AddSeconds(1), early.RetryAtUtc);

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(PhoneCodeSendOutcome.Sent, (await SendAsync()).Outcome);
        var second = await CodeAsync();

        if (first != second)
        {
            Assert.Equal(PhoneCodeVerifyOutcome.Wrong, (await VerifyAsync(first)).Outcome);
        }

        Assert.Equal(PhoneCodeVerifyOutcome.Verified, (await VerifyAsync(second)).Outcome);
    }

    [Fact]
    public async Task FourthSendInTheHour_LocksFor15Minutes()
    {
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(PhoneCodeSendOutcome.Sent, (await SendAsync()).Outcome);
            _clock.Advance(TimeSpan.FromMinutes(2));
        }

        var fourth = await SendAsync();
        Assert.Equal(PhoneCodeSendOutcome.Locked, fourth.Outcome);
        Assert.Equal(_clock.Now.UtcDateTime.AddMinutes(15), fourth.RetryAtUtc);

        _clock.Advance(TimeSpan.FromMinutes(14));
        Assert.Equal(PhoneCodeSendOutcome.Locked, (await SendAsync()).Outcome);
        Assert.Equal(3, await _factory.WithDbAsync(db => db.PhoneVerificationCodes.CountAsync(c => c.UserId == _scope.UserId)));
    }

    [Fact]
    public async Task ParallelSends_RespectTheCooldown()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => SendAsync()));

        Assert.Single(results, r => r.Outcome == PhoneCodeSendOutcome.Sent);
        Assert.Equal(1, await _factory.WithDbAsync(db => db.PhoneVerificationCodes.CountAsync(c => c.UserId == _scope.UserId)));
    }

    [Fact]
    public async Task ParallelWrongGuesses_NeverExceedFiveAttempts()
    {
        await SendAsync();
        var code = await CodeAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => VerifyAsync(Wrong(code))));

        Assert.Equal(4, results.Count(r => r.Outcome == PhoneCodeVerifyOutcome.Wrong));
        Assert.Equal(6, results.Count(r => r.Outcome == PhoneCodeVerifyOutcome.Locked));
        Assert.Equal(5, await _factory.WithDbAsync(db =>
            db.PhoneVerificationCodes.Where(c => c.UserId == _scope.UserId).Select(c => c.Attempts).SingleAsync()));
    }

    [Fact]
    public async Task CodeOfAnotherScope_DoesNotVerify()
    {
        await SendAsync();
        var code = await CodeAsync();
        var other = await _factory.CreateAccountAsync(AppRoles.Student);

        var result = await WithServiceAsync(s => s.VerifyAsync(_scope with { UserId = other.Id }, code, null, CancellationToken.None));

        Assert.Equal(PhoneCodeVerifyOutcome.Expired, result.Outcome);
    }
}
