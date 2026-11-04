using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PersonalHub.Web.Infrastructure.Observability;

namespace PersonalHub.IntegrationTests.Observability;

public class ObservabilityTests
{
    private const string ThrowPath = "/__test/throw";

    [Fact]
    public async Task Response_CarriesGeneratedCorrelationId()
    {
        await using var factory = CreateFactory("Testing");

        var response = await factory.CreateClient().GetAsync("/");

        var correlationId = Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName));
        Assert.True(CorrelationId.IsValid(correlationId));
    }

    [Fact]
    public async Task Response_EchoesValidIncomingCorrelationId()
    {
        await using var factory = CreateFactory("Testing");
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add(CorrelationId.HeaderName, "cliente-abc-123");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal("cliente-abc-123", Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName)));
    }

    [Theory]
    [InlineData("tem espaço")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 65 caracteres
    public async Task Response_ReplacesInvalidIncomingCorrelationId(string incoming)
    {
        await using var factory = CreateFactory("Testing");
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, incoming);

        var response = await factory.CreateClient().SendAsync(request);

        var correlationId = Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName));
        Assert.NotEqual(incoming, correlationId);
        Assert.True(CorrelationId.IsValid(correlationId));
    }

    [Fact]
    public async Task Production_UnhandledException_ReturnsGenericErrorWithCorrelationId()
    {
        await using var factory = CreateFactory("Production");
        var request = new HttpRequestMessage(HttpMethod.Get, ThrowPath);
        request.Headers.Add(CorrelationId.HeaderName, "erro-teste-1");

        var response = await factory.CreateClient().SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("erro-teste-1", Assert.Single(response.Headers.GetValues(CorrelationId.HeaderName)));
        Assert.Contains("Algo deu errado", body);
        Assert.Contains("erro-teste-1", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("detalhe-secreto", body);
        Assert.DoesNotContain("   at ", body);
    }

    [Fact]
    public async Task Production_UnhandledException_IsLoggedWithCorrelationId()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = CreateFactory("Production", logs);
        var request = new HttpRequestMessage(HttpMethod.Get, ThrowPath);
        request.Headers.Add(CorrelationId.HeaderName, "erro-teste-2");

        await factory.CreateClient().SendAsync(request);

        Assert.Contains(logs.Logs, log =>
            log.Level == LogLevel.Error && Equals(log.Scope.GetValueOrDefault("CorrelationId"), "erro-teste-2"));
    }

    [Fact]
    public async Task Request_IsLoggedOnceWithoutQueryString()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = CreateFactory("Testing", logs);
        var request = new HttpRequestMessage(HttpMethod.Get, "/?token=valor-secreto");
        request.Headers.Add(CorrelationId.HeaderName, "log-teste-1");

        await factory.CreateClient().SendAsync(request);

        var requestLog = Assert.Single(logs.Logs, log => log.Category == typeof(RequestLoggingMiddleware).FullName);
        Assert.Equal("log-teste-1", requestLog.Scope["CorrelationId"]);
        Assert.Contains("GET / respondeu 200", requestLog.Message);
        Assert.DoesNotContain(logs.Logs, log => log.Message.Contains("valor-secreto"));
    }

    [Fact]
    public async Task Request_HealthCheckIsNotLogged()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = CreateFactory("Testing", logs);

        await factory.CreateClient().GetAsync("/health");

        Assert.DoesNotContain(logs.Logs, log => log.Category == typeof(RequestLoggingMiddleware).FullName);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment, CapturingLoggerProvider? logs = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);

            // Banco inexistente: estes testes não dependem do PostgreSQL.
            builder.UseSetting("ConnectionStrings:PersonalHub", "Host=127.0.0.1;Port=1;Database=personalhub_tests;Username=x;Password=x;Timeout=1");

            builder.ConfigureServices(services =>
            {
                // Endpoint que lança exceção, existente só no host de teste. Entra depois do pipeline da
                // aplicação, então só roda quando nenhuma rota atende e a exceção passa pelo exception handler.
                services.AddTransient<IStartupFilter, ThrowingEndpointStartupFilter>();
            });

            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }
        });

    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((HttpContext context, RequestDelegate _) => context.Request.Path == ThrowPath
                ? throw new InvalidOperationException("detalhe-secreto que não pode aparecer")
                : Task.CompletedTask);
        };
    }
}
