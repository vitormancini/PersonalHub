using System.Diagnostics;

namespace PersonalHub.Web.Infrastructure.Observability;

/// <summary>
/// Uma linha de log por requisição HTTP: método, caminho, status e duração. O correlation ID vem do escopo.
/// Não registra query string, corpo nem headers, e troca segredos do caminho (o token em /convite/{token})
/// por "[redacted]".
/// </summary>
public partial class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldSkip(context.Request.Path))
        {
            await next(context);
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var statusCode = StatusCodes.Status500InternalServerError;
        try
        {
            await next(context);
            statusCode = context.Response.StatusCode;
        }
        finally
        {
            // Se a exceção escapar daqui (Development, sem exception handler), registra 500.
            var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogRequest(logger, context.Request.Method, SafePath(context.Request.Path),
                statusCode, Math.Round(elapsedMs, 1));
        }
    }

    // /health é chamado com frequência por monitoramento; arquivos estáticos e do framework só geram ruído.
    internal static bool ShouldSkip(PathString path) =>
        path.StartsWithSegments("/health")
        || path.StartsWithSegments("/_framework")
        || path.StartsWithSegments("/_content")
        || Path.HasExtension(path.Value);

    /// <summary>Caminho seguro para log: o token do convite dá acesso à criação da conta do aluno.</summary>
    internal static string SafePath(PathString path) =>
        path.StartsWithSegments("/convite", StringComparison.OrdinalIgnoreCase, out var rest) && rest.HasValue
            ? "/convite/[redacted]"
            : path.Value ?? "/";

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "HTTP {Method} {Path} respondeu {StatusCode} em {ElapsedMs} ms")]
    private static partial void LogRequest(ILogger logger, string method, string path, int statusCode, double elapsedMs);
}
