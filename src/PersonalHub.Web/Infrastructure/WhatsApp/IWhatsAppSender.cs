namespace PersonalHub.Web.Infrastructure.WhatsApp;

/// <summary>
/// Mensagem de template. <see cref="Code"/> só nos templates de autenticação: vai no corpo e no botão de copiar.
/// <see cref="ButtonUrlSuffix"/> completa o botão de URL do template (a base é <c>App:PublicBaseUrl</c>).
/// </summary>
public sealed record WhatsAppTemplateMessage(
    string ToE164,
    string Template,
    IReadOnlyList<string> BodyParameters,
    string? ButtonUrlSuffix,
    string? Code = null);

public enum WhatsAppSendStatus
{
    Sent,

    /// <summary>Falha passageira (429, 5xx, rede, 131000): tentar de novo mais tarde.</summary>
    Retryable,

    /// <summary>Não adianta repetir (número sem WhatsApp, template errado, parâmetro inválido).</summary>
    Permanent
}

/// <summary><see cref="Error"/> é sanitizado: só códigos, nunca texto do provedor, telefone ou token.</summary>
public sealed record WhatsAppSendResult(WhatsAppSendStatus Status, string? ProviderMessageId = null, string? Error = null)
{
    public static WhatsAppSendResult Sent(string? providerMessageId) => new(WhatsAppSendStatus.Sent, providerMessageId);
    public static WhatsAppSendResult Retry(string error) => new(WhatsAppSendStatus.Retryable, Error: error);
    public static WhatsAppSendResult Fail(string error) => new(WhatsAppSendStatus.Permanent, Error: error);
}

/// <summary>Adapter do provedor de WhatsApp (ARCHITECTURE §6). O domínio não conhece a Meta.</summary>
public interface IWhatsAppSender
{
    Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken);
}
