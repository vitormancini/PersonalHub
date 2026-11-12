using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;

namespace PersonalHub.Web.Features.Notifications;

/// <summary>
/// Conteúdo da linha da outbox: parâmetros do template, sufixo do botão de URL e, nos códigos, o código protegido pelo
/// Data Protection (nunca em texto puro no banco).
/// </summary>
public sealed record NotificationPayload(
    [property: JsonPropertyName("parameters")] IReadOnlyList<string> Parameters,
    [property: JsonPropertyName("button")] string? ButtonUrlSuffix,
    [property: JsonPropertyName("code")] string? ProtectedCode = null)
{
    /// <summary>Payload que fica depois do envio de um código: nada de segredo em repouso.</summary>
    public const string Cleared = "{}";

    public string ToJson() => JsonSerializer.Serialize(this);

    public static NotificationPayload? FromJson(string json)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<NotificationPayload>(json);
            return payload?.Parameters is null ? null : payload;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Protege o código que espera na outbox. Finalidade própria: o texto protegido não serve a nenhum outro uso.</summary>
public sealed class OutboxCodeProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("PersonalHub.Notifications.OutboxCode.v1");

    public string Protect(string code) => _protector.Protect(code);

    /// <summary>Nulo quando o texto não foi protegido por esta chave (chave trocada, linha adulterada).</summary>
    public string? Unprotect(string protectedCode)
    {
        try
        {
            return _protector.Unprotect(protectedCode);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
