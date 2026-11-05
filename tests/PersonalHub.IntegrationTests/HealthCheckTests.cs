using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PersonalHub.IntegrationTests.Infrastructure;

namespace PersonalHub.IntegrationTests;

[Collection(DatabaseCollection.Name)]
public class HealthCheckTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Health_ReturnsHealthy_WhenDatabaseIsReachable()
    {
        await using var factory = CreateFactory(fixture.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_ReturnsUnhealthy_WhenDatabaseIsUnreachable()
    {
        // Porta 1 não tem PostgreSQL: a conexão falha na hora.
        await using var factory = CreateFactory("Host=127.0.0.1;Port=1;Database=personalhub_tests;Username=x;Password=x;Timeout=2");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", await response.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Ambiente próprio para não carregar os user secrets de desenvolvimento.
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PersonalHub"] = connectionString,
                    [TestApp.PublicBaseUrlKey] = TestApp.PublicBaseUrl
                }));
        });
}
