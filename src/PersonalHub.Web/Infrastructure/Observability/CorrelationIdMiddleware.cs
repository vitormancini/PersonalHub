using System.Diagnostics;

namespace PersonalHub.Web.Infrastructure.Observability;

/// <summary>
/// Define o correlation ID da requisição, devolve no header <see cref="CorrelationId.HeaderName"/> e o coloca
/// num escopo de log, para que todas as linhas da requisição o carreguem.
/// </summary>
/// <remarks>
/// Blazor Server: os eventos de um circuito acontecem na conexão SignalR (<c>/_blazor</c>), não em novas
/// requisições HTTP. Por isso, os logs do circuito carregam o escopo da requisição que abriu a conexão.
/// </remarks>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = CorrelationId.Resolve(context.Request.Headers[CorrelationId.HeaderName], Activity.Current);

        // A página de erro lê o TraceIdentifier para mostrar o mesmo id ao usuário.
        context.TraceIdentifier = correlationId;

        // OnStarting sobrevive ao Clear() que o exception handler faz na resposta.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationId.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope("CorrelationId:{CorrelationId}", correlationId))
        {
            await next(context);
        }
    }
}
