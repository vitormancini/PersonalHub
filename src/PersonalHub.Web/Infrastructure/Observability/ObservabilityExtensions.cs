namespace PersonalHub.Web.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    public const string HostingDiagnosticsCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";

    /// <summary>
    /// Console legível em Development; JSON (uma linha por evento, UTC, com escopos) nos demais ambientes,
    /// pronto para um coletor de logs. Os níveis por categoria ficam nos appsettings de cada ambiente.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();

        // O escopo de log do hosting (RequestId, RequestPath) leva o caminho bruto para todas as linhas da
        // requisição, inclusive o token em /convite/{token}. Com a categoria desligada o escopo não é criado;
        // o correlation ID e o RequestLoggingMiddleware (com o caminho higienizado) cobrem o mesmo papel.
        // Registrado em código, depois da configuração, para nenhum appsettings religar por engano.
        builder.Logging.AddFilter(HostingDiagnosticsCategory, LogLevel.None);

        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddSimpleConsole(options =>
            {
                options.IncludeScopes = true;
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        }
        else
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            });
        }

        return builder;
    }

    /// <summary>Deve ser o primeiro middleware, para o correlation ID valer também nos logs de exceção.</summary>
    public static IApplicationBuilder UseObservability(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>()
            .UseMiddleware<RequestLoggingMiddleware>();
}
