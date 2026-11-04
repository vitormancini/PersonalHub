using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace PersonalHub.IntegrationTests.Infrastructure;

public static class TestApp
{
    /// <summary>
    /// Host de teste ligado ao banco de testes. O limite de login por IP fica alto para não interferir;
    /// o teste de rate limiting passa o próprio valor em <paramref name="settings"/>.
    /// </summary>
    public static WebApplicationFactory<Program> Create(
        string connectionString,
        IDictionary<string, string?>? settings = null,
        ILoggerProvider? logs = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Ambiente próprio para não carregar os user secrets de desenvolvimento.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:PersonalHub", connectionString);
            builder.UseSetting("Authentication:LoginRateLimit:PermitLimit", "1000");
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                builder.UseSetting(key, value);
            }

            if (logs is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
            }
        });

    /// <summary>
    /// Cliente HTTPS (o cookie de login é Secure e só volta em HTTPS), com cookies e sem seguir
    /// redirecionamentos, para os testes conferirem cada Location.
    /// </summary>
    public static HttpClient CreateBrowser(this WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
}
