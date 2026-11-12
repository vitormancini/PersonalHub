using Microsoft.Extensions.Options;

namespace PersonalHub.Web.Infrastructure.WhatsApp;

public static class WhatsAppServiceCollectionExtensions
{
    /// <summary>
    /// Escolhe o adapter por <c>WhatsApp:Provider</c> e valida a configuração na partida: com <c>Meta</c>, faltar
    /// token ou id do número impede o app de subir; <c>Log</c> não sobe em Production.
    /// </summary>
    public static WebApplicationBuilder AddWhatsApp(this WebApplicationBuilder builder)
    {
        var isProduction = builder.Environment.IsProduction();
        builder.Services.AddOptions<WhatsAppOptions>()
            .BindConfiguration(WhatsAppOptions.Section)
            .Validate(o => o.Validate(isProduction).Count == 0, "WhatsApp")
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<WhatsAppOptions>>(new DetailedValidation(isProduction));

        builder.Services.AddHttpClient<MetaCloudWhatsAppSender>((services, http) =>
        {
            http.BaseAddress = new Uri(MetaCloudWhatsAppSender.BaseAddress);
            http.Timeout = services.GetRequiredService<IOptions<WhatsAppOptions>>().Value.Timeout;
        });
        builder.Services.AddSingleton<LogWhatsAppSender>();
        builder.Services.AddTransient<IWhatsAppSender>(services =>
            services.GetRequiredService<IOptions<WhatsAppOptions>>().Value.IsMeta
                ? services.GetRequiredService<MetaCloudWhatsAppSender>()
                : services.GetRequiredService<LogWhatsAppSender>());

        builder.Services.AddWhatsAppWebhookRateLimiting();
        return builder;
    }

    /// <summary>Mensagens de erro específicas na partida (sem repetir valores secretos).</summary>
    private sealed class DetailedValidation(bool isProduction) : IValidateOptions<WhatsAppOptions>
    {
        public ValidateOptionsResult Validate(string? name, WhatsAppOptions options)
        {
            var errors = options.Validate(isProduction);
            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }
    }
}
