using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PersonalHub.Web.Domain.Notifications;

namespace PersonalHub.Web.Infrastructure.WhatsApp;

/// <summary>
/// WhatsApp Cloud API da Meta, sem intermediário (D-055): <c>POST /{versão}/{PhoneNumberId}/messages</c> com mensagem
/// de template. Nada do corpo da resposta vai para o log: só o status HTTP e o código de erro da Meta.
/// </summary>
public sealed partial class MetaCloudWhatsAppSender(
    HttpClient http, IOptions<WhatsAppOptions> options, ILogger<MetaCloudWhatsAppSender> logger) : IWhatsAppSender
{
    public const string BaseAddress = "https://graph.facebook.com/";

    public async Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.ApiVersion}/{settings.PhoneNumberId}/messages")
        {
            Content = JsonContent(BuildBody(message))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(logger, message.Template, 0, "timeout");
            return WhatsAppSendResult.Retry("timeout");
        }
        catch (HttpRequestException)
        {
            LogFailure(logger, message.Template, 0, "rede");
            return WhatsAppSendResult.Retry("rede");
        }

        using (response)
        {
            var json = await ReadJsonAsync(response, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var id = json?["messages"]?[0]?["id"]?.GetValue<string>();
                if (string.IsNullOrEmpty(id))
                {
                    // Aceito sem id: não dá para acompanhar a entrega, mas a mensagem saiu. Não reenviar (duplicaria).
                    LogFailure(logger, message.Template, (int)response.StatusCode, "sem-id");
                }

                return WhatsAppSendResult.Sent(id);
            }

            var code = json?["error"]?["code"]?.GetValueKind() == JsonValueKind.Number
                ? json["error"]!["code"]!.GetValue<int>()
                : (int?)null;
            var result = Classify(response.StatusCode, code);
            LogFailure(logger, message.Template, (int)response.StatusCode, result.Error!);
            return result;
        }
    }

    /// <summary>
    /// Corpo da mensagem. Template de autenticação: o código no corpo e no botão de copiar (sub_type url, índice 0).
    /// Demais: parâmetros do corpo e o sufixo do botão de URL.
    /// </summary>
    public static JsonObject BuildBody(WhatsAppTemplateMessage message)
    {
        var bodyParameters = message.Code is not null ? [message.Code] : message.BodyParameters;
        var components = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "body",
                ["parameters"] = new JsonArray(bodyParameters.Select(p => (JsonNode)Text(p)).ToArray())
            }
        };

        var buttonText = message.Code ?? message.ButtonUrlSuffix;
        if (buttonText is not null)
        {
            components.Add(new JsonObject
            {
                ["type"] = "button",
                ["sub_type"] = "url",
                ["index"] = "0",
                ["parameters"] = new JsonArray(Text(buttonText))
            });
        }

        return new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            // A Cloud API recebe o número só com dígitos (DDI + DDD + número).
            ["to"] = message.ToE164.TrimStart('+'),
            ["type"] = "template",
            ["template"] = new JsonObject
            {
                ["name"] = message.Template,
                ["language"] = new JsonObject { ["code"] = NotificationTemplates.Language },
                ["components"] = components
            }
        };
    }

    /// <summary>
    /// Falha passageira ou definitiva. 429, 5xx e os códigos de instabilidade/limite da Meta são repetidos; número que
    /// não recebe (131026), conta bloqueada (131031), parâmetro inválido (100) e erros de template (132xxx) não.
    /// </summary>
    public static WhatsAppSendResult Classify(HttpStatusCode status, int? metaCode)
    {
        var error = metaCode is { } c
            ? $"meta:{c.ToString(CultureInfo.InvariantCulture)}"
            : $"http:{((int)status).ToString(CultureInfo.InvariantCulture)}";

        if (metaCode is { } code)
        {
            if (code is 131026 or 131031 or 100 or 131008 or 131009 or 131051 or 133010 or >= 132000 and < 133000)
            {
                return WhatsAppSendResult.Fail(error);
            }

            // 131000 erro genérico; 131016 serviço indisponível; 130429/131048/131056/80007/4 limites de envio;
            // 1 e 2 instabilidade da API; 190 token expirado (dá tempo de trocar o token antes de esgotar as tentativas).
            if (code is 131000 or 131016 or 130429 or 131048 or 131056 or 80007 or 4 or 1 or 2 or 190)
            {
                return WhatsAppSendResult.Retry(error);
            }
        }

        return status == HttpStatusCode.TooManyRequests || (int)status >= 500
            ? WhatsAppSendResult.Retry(error)
            : WhatsAppSendResult.Fail(error);
    }

    private static StringContent JsonContent(JsonObject body) =>
        new(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

    private static JsonObject Text(string value) => new() { ["type"] = "text", ["text"] = value };

    private static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(EventId = 7110, Level = LogLevel.Warning,
        Message = "WhatsApp (Meta) {Template} não enviado: HTTP {Status}, {Error}")]
    private static partial void LogFailure(ILogger logger, string template, int status, string error);
}
