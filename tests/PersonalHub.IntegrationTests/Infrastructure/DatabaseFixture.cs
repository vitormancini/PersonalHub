using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.IntegrationTests.Infrastructure;

/// <summary>
/// Cria um banco de testes separado, aplica as migrations no início e apaga o banco no fim.
/// Connection string: variável de ambiente <c>ConnectionStrings__PersonalHubTests</c> ou user secrets do projeto.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string ConnectionStringName = "PersonalHubTests";

    public string ConnectionString { get; }

    public DatabaseFixture()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<DatabaseFixture>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' não configurada. Veja a seção de testes do README.");
        }

        // Proteção: o fixture apaga o banco ao terminar, então só aceita bancos de teste.
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        if (database is null || !database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"O banco de testes precisa ter 'test' no nome; recebido '{database}'.");
        }

        ConnectionString = connectionString;
    }

    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new AppDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
