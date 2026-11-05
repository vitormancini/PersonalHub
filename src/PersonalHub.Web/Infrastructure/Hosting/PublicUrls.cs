using Microsoft.Extensions.Options;

namespace PersonalHub.Web.Infrastructure.Hosting;

public sealed class PublicUrlOptions
{
    public const string Section = "App";

    /// <summary>Endereço público da aplicação (ex.: https://app.exemplo.com). Obrigatório fora de Development.</summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>
/// Monta links absolutos enviados para fora da aplicação, como o do convite. Usa o endereço configurado em vez do
/// header Host da requisição, que o cliente controla (host header injection).
/// </summary>
public sealed class PublicUrls(IOptions<PublicUrlOptions> options)
{
    private readonly Uri? _baseUrl = TryParse(options.Value.PublicBaseUrl);

    /// <summary>
    /// <paramref name="requestBaseUri"/> só é usado em Development sem configuração; nos demais ambientes a
    /// inicialização falha antes (<see cref="PublicUrlServiceCollectionExtensions.AddPublicUrls"/>).
    /// </summary>
    public string Absolute(string path, string requestBaseUri) =>
        new Uri(_baseUrl ?? new Uri(requestBaseUri), path).ToString();

    /// <summary>URL absoluta https, só com host (e porta): sem caminho, query, fragmento ou credenciais.</summary>
    public static Uri? TryParse(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.AbsolutePath == "/" &&
        uri.Query.Length == 0 &&
        uri.Fragment.Length == 0 &&
        uri.UserInfo.Length == 0
            ? uri
            : null;
}

public static class PublicUrlServiceCollectionExtensions
{
    public const string ConfigurationKey = $"{PublicUrlOptions.Section}:{nameof(PublicUrlOptions.PublicBaseUrl)}";

    /// <summary>
    /// Fora de Development, a aplicação não sobe sem <c>App:PublicBaseUrl</c> válida. Em Development ela é opcional
    /// e, sem ela, os links usam o endereço da requisição.
    /// </summary>
    public static WebApplicationBuilder AddPublicUrls(this WebApplicationBuilder builder)
    {
        var required = !builder.Environment.IsDevelopment();

        builder.Services.AddOptions<PublicUrlOptions>()
            .BindConfiguration(PublicUrlOptions.Section)
            .Validate(
                o => PublicUrls.TryParse(o.PublicBaseUrl) is not null ||
                     (!required && string.IsNullOrWhiteSpace(o.PublicBaseUrl)),
                $"{ConfigurationKey} precisa ser uma URL https absoluta, sem caminho (ex.: https://app.exemplo.com). " +
                "É obrigatória fora de Development.")
            .ValidateOnStart();
        builder.Services.AddSingleton<PublicUrls>();

        return builder;
    }
}
