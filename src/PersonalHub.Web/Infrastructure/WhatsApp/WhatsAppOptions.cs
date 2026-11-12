using System.Text.RegularExpressions;

namespace PersonalHub.Web.Infrastructure.WhatsApp;

/// <summary>
/// Configuração do WhatsApp (D-055). Segredos (<see cref="AccessToken"/>, <see cref="AppSecret"/>,
/// <see cref="WebhookVerifyToken"/>) vêm de user-secrets ou variáveis de ambiente, nunca do repositório.
/// </summary>
public sealed partial class WhatsAppOptions
{
    public const string Section = "WhatsApp";
    public const string LogProvider = "Log";
    public const string MetaProvider = "Meta";

    /// <summary><c>Log</c> (padrão; só registra no log) ou <c>Meta</c> (WhatsApp Cloud API).</summary>
    public string Provider { get; set; } = LogProvider;

    /// <summary>Versão da Graph API, por exemplo <c>v23.0</c>.</summary>
    public string ApiVersion { get; set; } = "v23.0";

    /// <summary>Id do número de telefone remetente na Meta (não é o número em si).</summary>
    public string? PhoneNumberId { get; set; }

    public string? AccessToken { get; set; }

    /// <summary>Segredo do app na Meta: valida a assinatura do webhook. Vazio desliga o webhook (404).</summary>
    public string? AppSecret { get; set; }

    /// <summary>Texto combinado com a Meta na verificação do webhook (GET com <c>hub.verify_token</c>).</summary>
    public string? WebhookVerifyToken { get; set; }

    /// <summary>Limite de tempo por chamada à Meta.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    public bool IsMeta => string.Equals(Provider, MetaProvider, StringComparison.OrdinalIgnoreCase);
    public bool IsLog => string.Equals(Provider, LogProvider, StringComparison.OrdinalIgnoreCase);
    public bool WebhookEnabled => !string.IsNullOrWhiteSpace(AppSecret);

    /// <summary>Erros de configuração; vazio quando está tudo certo. Nenhuma mensagem repete o valor de um segredo.</summary>
    public IReadOnlyList<string> Validate(bool isProduction)
    {
        var errors = new List<string>();
        if (!IsLog && !IsMeta)
        {
            errors.Add($"{Section}:Provider deve ser '{LogProvider}' ou '{MetaProvider}'.");
            return errors;
        }

        if (IsLog && isProduction)
        {
            errors.Add($"{Section}:Provider = '{LogProvider}' não é permitido em Production: o log mostraria os códigos.");
        }

        if (IsMeta)
        {
            if (!ApiVersionPattern().IsMatch(ApiVersion ?? string.Empty))
            {
                errors.Add($"{Section}:ApiVersion precisa ser como 'v23.0'.");
            }

            if (string.IsNullOrWhiteSpace(PhoneNumberId) || !PhoneNumberId.All(char.IsAsciiDigit))
            {
                errors.Add($"{Section}:PhoneNumberId é obrigatório com o provider Meta (só dígitos).");
            }

            if (string.IsNullOrWhiteSpace(AccessToken))
            {
                errors.Add($"{Section}:AccessToken é obrigatório com o provider Meta.");
            }

            if (WebhookEnabled && string.IsNullOrWhiteSpace(WebhookVerifyToken))
            {
                errors.Add($"{Section}:WebhookVerifyToken é obrigatório quando {Section}:AppSecret está configurado.");
            }
        }

        return errors;
    }

    [GeneratedRegex(@"^v\d{1,3}\.\d{1,3}$")]
    private static partial Regex ApiVersionPattern();
}
