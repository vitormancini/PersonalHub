using Microsoft.EntityFrameworkCore;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public class MigrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Migrations_AreAllApplied()
    {
        await using var context = fixture.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Contains(await context.Database.GetAppliedMigrationsAsync(), id => id.EndsWith("_InitialSchema"));
    }

    [Fact]
    public async Task Roles_AreSeeded()
    {
        await using var context = fixture.CreateContext();

        var roles = await context.Roles.OrderBy(r => r.Name).Select(r => new { r.Id, r.Name }).ToListAsync();

        Assert.Equal(
            [new { Id = AppRoles.PersonalId, Name = (string?)AppRoles.Personal }, new { Id = AppRoles.StudentId, Name = (string?)AppRoles.Student }],
            roles);
    }
}
