using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace PersonalHub.Web.Features.Students.AcceptInvite;

public sealed class InviteRateLimitOptions
{
    public const string SectionName = "Students:InviteRateLimit";

    public int PermitLimit { get; set; } = 20;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Limita GET e POST em /convite/{token} por IP (janela fixa), aplicado pela página com
/// <c>[EnableRateLimiting]</c>. Freia quem tenta adivinhar tokens e quem testa senhas pelo formulário de
/// conta existente (que também conta para o bloqueio da conta, como o login).
/// </summary>
public static partial class InviteRateLimiting
{
    public const string Policy = "invite";

    public static IServiceCollection AddInviteRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<InviteRateLimitOptions>().BindConfiguration(InviteRateLimitOptions.SectionName);
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<InviteRateLimitOptions>>((options, invite) =>
                options.AddPolicy(Policy, new InvitePolicy(invite.Value)));

        return services;
    }

    private sealed class InvitePolicy(InviteRateLimitOptions limits) : IRateLimiterPolicy<string>
    {
        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => OnRejectedAsync;

        public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.PermitLimit,
                    Window = limits.Window,
                    QueueLimit = 0
                });
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(InviteRateLimiting).FullName!);
        LogRejected(logger);

        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        await context.HttpContext.Response.WriteAsync(
            "Muitas tentativas. Aguarde um minuto e tente de novo.", cancellationToken);
    }

    [LoggerMessage(EventId = 3110, Level = LogLevel.Warning,
        Message = "Convite: limite de requisições por IP atingido")]
    private static partial void LogRejected(ILogger logger);
}
