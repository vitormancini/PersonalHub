using Microsoft.Extensions.Options;
using PersonalHub.Web.Infrastructure.Hosting;

namespace PersonalHub.Web.Infrastructure.WhatsApp;

/// <summary>
/// Provider <c>Log</c> (padrão em Development): não envia nada, só registra template, parâmetros e o link do botão,
/// com o telefone mascarado. Os códigos aparecem no log de propósito, para testar o convite e a senha localmente;
/// por isso este provider não sobe em Production (<see cref="WhatsAppOptions.Validate"/>).
/// </summary>
public sealed partial class LogWhatsAppSender(IOptions<PublicUrlOptions> urls, ILogger<LogWhatsAppSender> logger)
    : IWhatsAppSender
{
    public Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken)
    {
        var parameters = message.Code is not null ? [message.Code] : message.BodyParameters;
        var button = message.ButtonUrlSuffix is null
            ? null
            : (urls.Value.PublicBaseUrl?.TrimEnd('/') ?? "https://localhost") + "/" + message.ButtonUrlSuffix;
        var id = "log." + Guid.NewGuid().ToString("N");

        LogMessage(logger, message.Template, PhoneLogMask.Mask(message.ToE164), string.Join(" | ", parameters), button, id);
        return Task.FromResult(WhatsAppSendResult.Sent(id));
    }

    [LoggerMessage(EventId = 7100, Level = LogLevel.Information,
        Message = "WhatsApp (provider Log) {Template} para {Phone}: [{Parameters}] botão {Button} ({ProviderMessageId})")]
    private static partial void LogMessage(
        ILogger logger, string template, string phone, string parameters, string? button, string providerMessageId);
}
