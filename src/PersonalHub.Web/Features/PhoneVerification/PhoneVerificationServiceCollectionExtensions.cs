using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace PersonalHub.Web.Features.PhoneVerification;

public sealed class PhoneCodeRateLimitOptions
{
    public const string SectionName = "PhoneVerification:RateLimit";

    public int PermitLimit { get; set; } = 20;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

public static partial class PhoneVerificationServiceCollectionExtensions
{
    /// <summary>Política por IP das páginas de código (esqueci a senha, nova senha, confirmar celular).</summary>
    public const string RateLimitPolicy = "phone-code";

    public static WebApplicationBuilder AddPhoneVerification(this WebApplicationBuilder builder)
    {
        var keyRequired = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
        builder.Services.AddOptions<PhoneVerificationOptions>()
            .BindConfiguration(PhoneVerificationOptions.Section)
            .Validate(o => !keyRequired || o.KeyBytes() is not null,
                "PhoneVerification:HashKey precisa ser base64 de 32 bytes ou mais (gere com: openssl rand -base64 32). " +
                "É obrigatória fora de Development.")
            .ValidateOnStart();
        builder.Services.AddSingleton<PhoneCodeHasher>();
        builder.Services.AddScoped<PhoneCodeService>();

        builder.Services.AddOptions<PhoneCodeRateLimitOptions>().BindConfiguration(PhoneCodeRateLimitOptions.SectionName);
        builder.Services.AddRateLimiter(_ => { });
        builder.Services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<PhoneCodeRateLimitOptions>>((options, limits) =>
                options.AddPolicy(RateLimitPolicy, new Policy(limits.Value)));
        return builder;
    }

    private sealed class Policy(PhoneCodeRateLimitOptions limits) : IRateLimiterPolicy<string>
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
            .CreateLogger(typeof(PhoneVerificationServiceCollectionExtensions).FullName!);
        LogRejected(logger);

        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        await context.HttpContext.Response.WriteAsync(
            "Muitas tentativas. Aguarde um minuto e tente de novo.", cancellationToken);
    }

    [LoggerMessage(EventId = 7310, Level = LogLevel.Warning, Message = "Páginas de código: limite de requisições por IP atingido")]
    private static partial void LogRejected(ILogger logger);
}
