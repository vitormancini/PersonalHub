using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace PersonalHub.Web.Features.Authentication.Login;

public sealed class LoginRateLimitOptions
{
    public const string SectionName = "Authentication:LoginRateLimit";

    public int PermitLimit { get; set; } = 10;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Limita os POSTs em /entrar por IP (janela fixa). Complementa o bloqueio por conta do Identity: este
/// freia quem testa muitos e-mails diferentes a partir do mesmo endereço.
/// </summary>
/// <remarks>
/// Atrás de um proxy/load balancer (AWS), o IP remoto será o do proxy até configurarmos ForwardedHeaders.
/// </remarks>
public static partial class LoginRateLimiting
{
    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<LoginRateLimitOptions>().BindConfiguration(LoginRateLimitOptions.SectionName);

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<LoginRateLimitOptions>>((options, login) =>
            {
                var limits = login.Value;
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                    IsLoginPost(http.Request)
                        ? RateLimitPartition.GetFixedWindowLimiter(
                            http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                            _ => new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = limits.PermitLimit,
                                Window = limits.Window,
                                QueueLimit = 0
                            })
                        : RateLimitPartition.GetNoLimiter(string.Empty));
                options.OnRejected = OnRejectedAsync;
            });

        return services;
    }

    private static bool IsLoginPost(HttpRequest request) =>
        HttpMethods.IsPost(request.Method)
        && request.Path.Equals("/entrar", StringComparison.OrdinalIgnoreCase);

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(LoginRateLimiting).FullName!);
        LogRejected(logger);

        // Com corpo, a página de status (UseStatusCodePagesWithReExecute) não substitui a resposta.
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync(
            "Muitas tentativas de login. Aguarde um minuto e tente de novo.", cancellationToken);
    }

    [LoggerMessage(EventId = 2010, Level = LogLevel.Warning,
        Message = "Login recusado: limite de tentativas por IP atingido")]
    private static partial void LogRejected(ILogger logger);
}
