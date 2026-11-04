using Microsoft.EntityFrameworkCore;

namespace PersonalHub.Web.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "PersonalHub";

    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        // A connection string é lida ao criar o DbContext, assim testes e ferramentas podem sobrescrevê-la.
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider.GetRequiredService<IConfiguration>()
                .GetConnectionString(ConnectionStringName);

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{ConnectionStringName}' não configurada. Veja o README (dotnet user-secrets).");
            }

            options.UseNpgsql(connectionString);
        });

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database");

        return services;
    }
}
