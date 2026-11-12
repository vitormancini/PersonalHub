using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Infrastructure.WhatsApp;

public sealed class WebhookRateLimitOptions
{
    public const string SectionName = "WhatsApp:WebhookRateLimit";

    public int PermitLimit { get; set; } = 120;
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Uma atualização de entrega lida do webhook.</summary>
public sealed record DeliveryUpdate(string ProviderMessageId, NotificationDeliveryStatus Status, DateTime AtUtc, int? ErrorCode);

/// <summary>
/// Webhook da Meta em <c>/webhooks/whatsapp</c> (D-062). Público (a Meta não tem login), sem antiforgery (não há
/// formulário nem cookie) e com limite por IP. Só existe com <c>WhatsApp:AppSecret</c> configurado; sem ele, 404.
/// GET: verificação da assinatura do webhook. POST: assinatura <c>X-Hub-Signature-256</c> (HMAC-SHA256 do corpo com o
/// AppSecret, comparada em tempo constante) e atualização da entrega pelo id da mensagem. Repetir a mesma notificação
/// não muda nada: a situação só avança (enviada → entregue → lida).
/// </summary>
public static partial class WhatsAppWebhook
{
    public const string Path = "/webhooks/whatsapp";
    public const string RateLimitPolicy = "whatsapp-webhook";
    public const int MaxBodyBytes = 256 * 1024;

    public static IServiceCollection AddWhatsAppWebhookRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<WebhookRateLimitOptions>().BindConfiguration(WebhookRateLimitOptions.SectionName);
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<WebhookRateLimitOptions>>((options, limits) =>
                options.AddPolicy(RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.Value.PermitLimit,
                        Window = limits.Value.Window,
                        QueueLimit = 0
                    })));
        return services;
    }

    public static IEndpointRouteBuilder MapWhatsAppWebhook(this IEndpointRouteBuilder endpoints)
    {
        foreach (var endpoint in new[] { endpoints.MapGet(Path, Verify), endpoints.MapPost(Path, ReceiveAsync) })
        {
            endpoint.AllowAnonymous().DisableAntiforgery().RequireRateLimiting(RateLimitPolicy)
                // Respostas de API: 401/404 sem corpo não devem virar a página de erro HTML (re-execução de status).
                .AddEndpointFilter(async (context, next) =>
                {
                    if (context.HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } pages)
                    {
                        pages.Enabled = false;
                    }

                    return await next(context);
                });
        }

        return endpoints;
    }

    private static IResult Verify(HttpContext http, IOptions<WhatsAppOptions> options)
    {
        var settings = options.Value;
        if (!settings.WebhookEnabled)
        {
            return Results.NotFound();
        }

        var query = http.Request.Query;
        var mode = query["hub.mode"].ToString();
        var token = query["hub.verify_token"].ToString();
        var challenge = query["hub.challenge"].ToString();
        if (mode == "subscribe" && challenge.Length is > 0 and <= 200 &&
            FixedTimeEquals(token, settings.WebhookVerifyToken ?? string.Empty))
        {
            return Results.Text(challenge, "text/plain");
        }

        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ReceiveAsync(
        HttpContext http, IOptions<WhatsAppOptions> options, AppDbContext db, TimeProvider clock,
        ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.WebhookEnabled)
        {
            return Results.NotFound();
        }

        var logger = loggers.CreateLogger(typeof(WhatsAppWebhook).FullName!);
        if (http.Request.ContentLength > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = await ReadBodyAsync(http.Request, cancellationToken);
        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (!IsSignatureValid(body, http.Request.Headers["X-Hub-Signature-256"].ToString(), settings.AppSecret!))
        {
            LogInvalidSignature(logger);
            return Results.Unauthorized();
        }

        IReadOnlyList<DeliveryUpdate> updates;
        try
        {
            updates = ParseStatuses(body);
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        var applied = await ApplyAsync(db, updates, clock.GetUtcNow().UtcDateTime, cancellationToken);
        LogReceived(logger, updates.Count, applied);
        // 200 mesmo sem mensagem conhecida: senão a Meta repete a entrega do evento por horas.
        return Results.Ok();
    }

    /// <summary><c>sha256=&lt;hex&gt;</c> calculado com o AppSecret sobre os bytes exatos do corpo.</summary>
    public static bool IsSignatureValid(byte[] body, string? header, string appSecret)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrEmpty(header) || !header.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] received;
        try
        {
            received = Convert.FromHexString(header.AsSpan(prefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    public static string Sign(byte[] body, string appSecret) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));

    /// <summary>Lê <c>entry[].changes[].value.statuses[]</c>. Mensagens recebidas e outros campos são ignorados.</summary>
    public static IReadOnlyList<DeliveryUpdate> ParseStatuses(byte[] body)
    {
        var updates = new List<DeliveryUpdate>();
        var root = JsonNode.Parse(body) as JsonObject;
        foreach (var entry in root?["entry"] as JsonArray ?? [])
        {
            foreach (var change in entry?["changes"] as JsonArray ?? [])
            {
                foreach (var status in change?["value"]?["statuses"] as JsonArray ?? [])
                {
                    var id = status?["id"]?.GetValueKind() == JsonValueKind.String ? status["id"]!.GetValue<string>() : null;
                    var kind = status?["status"]?.GetValueKind() == JsonValueKind.String
                        ? status["status"]!.GetValue<string>()
                        : null;
                    NotificationDeliveryStatus? parsed = kind switch
                    {
                        "sent" => NotificationDeliveryStatus.Sent,
                        "delivered" => NotificationDeliveryStatus.Delivered,
                        "read" => NotificationDeliveryStatus.Read,
                        "failed" => NotificationDeliveryStatus.Failed,
                        _ => null
                    };
                    if (string.IsNullOrEmpty(id) || id.Length > 128 || parsed is null)
                    {
                        continue;
                    }

                    var at = long.TryParse(status!["timestamp"]?.ToString(), out var seconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
                        : (DateTime?)null;
                    var error = status["errors"]?[0]?["code"]?.GetValueKind() == JsonValueKind.Number
                        ? status["errors"]![0]!["code"]!.GetValue<int>()
                        : (int?)null;
                    updates.Add(new DeliveryUpdate(id, parsed.Value, at ?? DateTime.MinValue, error));
                }
            }
        }

        return updates;
    }

    /// <summary>Aplica as atualizações; devolve quantas mensagens mudaram.</summary>
    public static async Task<int> ApplyAsync(
        AppDbContext db, IReadOnlyList<DeliveryUpdate> updates, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (updates.Count == 0)
        {
            return 0;
        }

        var ids = updates.Select(u => u.ProviderMessageId).Distinct().ToList();
        var rows = await db.NotificationOutbox.Where(o => o.ProviderMessageId != null && ids.Contains(o.ProviderMessageId))
            .ToListAsync(cancellationToken);
        var changed = 0;
        foreach (var row in rows)
        {
            var before = (row.DeliveryStatus, row.DeliveredAtUtc, row.ReadAtUtc);
            foreach (var update in updates.Where(u => u.ProviderMessageId == row.ProviderMessageId))
            {
                Apply(row, update, nowUtc);
            }

            if (before != (row.DeliveryStatus, row.DeliveredAtUtc, row.ReadAtUtc))
            {
                changed++;
                if (row.DeliveryStatus == NotificationDeliveryStatus.Failed && before.DeliveryStatus != NotificationDeliveryStatus.Failed)
                {
                    db.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        Action = "NotificationDeliveryFailed",
                        EntityType = nameof(NotificationOutbox),
                        EntityId = row.Id,
                        OccurredAtUtc = nowUtc,
                        Metadata = JsonSerializer.Serialize(new { template = row.EventType, reason = row.LastError }),
                        CreatedAtUtc = nowUtc
                    });
                }
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // O worker mexeu na mesma linha agora: a Meta reenvia o evento se não houver 200, mas aqui já respondemos
            // 200; a situação de entrega é informativa, então perder esta atualização não altera nenhuma regra.
            db.ChangeTracker.Clear();
            return 0;
        }

        return changed;
    }

    /// <summary>Só avança: lida não volta para entregue; falha só vale antes da entrega.</summary>
    public static void Apply(NotificationOutbox row, DeliveryUpdate update, DateTime nowUtc)
    {
        var at = update.AtUtc == DateTime.MinValue ? nowUtc : update.AtUtc;
        switch (update.Status)
        {
            case NotificationDeliveryStatus.Read:
                row.ReadAtUtc ??= at;
                row.DeliveredAtUtc ??= at;
                row.DeliveryStatus = NotificationDeliveryStatus.Read;
                break;
            case NotificationDeliveryStatus.Delivered:
                row.DeliveredAtUtc ??= at;
                if (row.DeliveryStatus is null or NotificationDeliveryStatus.Sent or NotificationDeliveryStatus.Failed)
                {
                    row.DeliveryStatus = NotificationDeliveryStatus.Delivered;
                }

                break;
            case NotificationDeliveryStatus.Sent:
                row.DeliveryStatus ??= NotificationDeliveryStatus.Sent;
                break;
            case NotificationDeliveryStatus.Failed:
                if (row.DeliveryStatus is null or NotificationDeliveryStatus.Sent)
                {
                    row.DeliveryStatus = NotificationDeliveryStatus.Failed;
                    row.LastError = update.ErrorCode is { } code ? $"meta:{code}" : "entrega";
                }

                break;
        }
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var left = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var right = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(left, right) && b.Length > 0;
    }

    [LoggerMessage(EventId = 7200, Level = LogLevel.Warning, Message = "Webhook do WhatsApp com assinatura inválida")]
    private static partial void LogInvalidSignature(ILogger logger);

    [LoggerMessage(EventId = 7201, Level = LogLevel.Information,
        Message = "Webhook do WhatsApp: {Updates} atualizações de entrega, {Applied} mensagens alteradas")]
    private static partial void LogReceived(ILogger logger, int updates, int applied);
}
