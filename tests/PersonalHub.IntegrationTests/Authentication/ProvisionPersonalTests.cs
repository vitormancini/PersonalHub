using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Features.Authentication.ProvisionPersonal;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.IntegrationTests.Authentication;

[Collection(DatabaseCollection.Name)]
public sealed class ProvisionPersonalTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = default!;

    public Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Provision_CreatesUserRoleProfileAndAudit_AndPersonalCanLogIn()
    {
        var email = NewEmail();

        var result = await ProvisionAsync(new ProvisionPersonalRequest(email, "  Ana Personal ", "+5511988887777", "Senha-Forte1"));

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.Equal(result.UserId, user.Id);
            Assert.True(await users.IsInRoleAsync(user, AppRoles.Personal));
            Assert.True(user.LockoutEnabled);

            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var profile = await db.PersonalProfiles.SingleAsync(p => p.UserId == user.Id);
            Assert.Equal("Ana Personal", profile.DisplayName);
            Assert.Equal("+5511988887777", profile.PhoneNumber);
            Assert.True(profile.IsActive);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == profile.Id && a.Action == "PersonalProvisioned"));
        }

        var client = _factory.CreateBrowser();
        var login = await client.PostLoginAsync(email, "Senha-Forte1");
        Assert.Equal("/personal", login.RedirectTarget());
    }

    [Theory]
    [InlineData("curta1A")]
    [InlineData("semnumeroA")]
    [InlineData("SEMMINUSCULA1")]
    [InlineData("semmaiuscula1")]
    public async Task Provision_RejectsWeakPassword_AndCreatesNothing(string password)
    {
        var email = NewEmail();

        var result = await ProvisionAsync(new ProvisionPersonalRequest(email, "Ana", null, password));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.StartsWith("A senha precisa", StringComparison.Ordinal));
        await AssertNothingCreatedAsync(email);
    }

    [Fact]
    public async Task Provision_RejectsDuplicateEmail()
    {
        var email = NewEmail();
        Assert.True((await ProvisionAsync(new ProvisionPersonalRequest(email, "Ana", null, "Senha-Forte1"))).Succeeded);

        var second = await ProvisionAsync(new ProvisionPersonalRequest(email.ToUpperInvariant(), "Outra", null, "Senha-Forte1"));

        Assert.False(second.Succeeded);
        Assert.Contains("Já existe uma conta com este e-mail.", second.Errors);
    }

    [Theory]
    [InlineData("nao-e-email", "Ana", null)]
    [InlineData("ok@example.test", "  ", null)]
    [InlineData("ok@example.test", "Ana", "11999990000")]
    public async Task Provision_RejectsInvalidInput(string email, string name, string? phone)
    {
        var uniqueEmail = email.Replace("ok@", $"ok-{Guid.NewGuid():N}@");

        var result = await ProvisionAsync(new ProvisionPersonalRequest(uniqueEmail, name, phone, "Senha-Forte1"));

        Assert.False(result.Succeeded);
        await AssertNothingCreatedAsync(uniqueEmail);
    }

    private async Task<ProvisionPersonalResult> ProvisionAsync(ProvisionPersonalRequest request)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<ProvisionPersonalHandler>(scope.ServiceProvider);
        return await handler.HandleAsync(request, CancellationToken.None);
    }

    private async Task AssertNothingCreatedAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant()));
    }

    private static string NewEmail() => $"personal-{Guid.NewGuid():N}@example.test";
}
