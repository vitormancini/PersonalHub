using Microsoft.AspNetCore.Authorization;
using PersonalHub.Web.Components;
using PersonalHub.Web.Features.Authentication.Login;
using PersonalHub.Web.Features.Authentication.Logout;
using PersonalHub.Web.Features.Authentication.ProvisionPersonal;
using PersonalHub.Web.Features.Billing;
using PersonalHub.Web.Features.Billing.CloseBilling;
using PersonalHub.Web.Features.Lessons;
using PersonalHub.Web.Features.Notifications;
using PersonalHub.Web.Features.PhoneVerification;
using PersonalHub.Web.Features.PhoneVerification.ConfirmPhone;
using PersonalHub.Web.Features.PasswordReset;
using PersonalHub.Web.Features.Students;
using PersonalHub.Web.Infrastructure.Hosting;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Observability;
using PersonalHub.Web.Infrastructure.Persistence;
using PersonalHub.Web.Infrastructure.WhatsApp;

// Comando administrativo (D-027): roda e encerra sem iniciar o servidor web.
if (ProvisionPersonalCommand.IsInvoked(args))
{
    return await ProvisionPersonalCommand.RunAsync(args);
}

// Fechamento manual (D-047): roda uma vez e encerra. Útil para testar um vencimento localmente.
if (CloseBillingCommand.IsInvoked(args))
{
    return await CloseBillingCommand.RunAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.AddPublicUrls();
builder.AddWhatsApp();
builder.AddPhoneVerification();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddPersistence();
builder.Services.AddAppIdentityCore();
builder.Services.AddAppAuthentication();
builder.Services.AddLoginRateLimiting();
builder.Services.AddScoped<LoginHandler>();
builder.Services.AddStudents();
builder.Services.AddLessons();
builder.Services.AddBilling(builder.Configuration);
builder.Services.AddNotifications(builder.Configuration);
builder.Services.AddPasswordReset();
builder.Services.AddScoped<ConfirmPhoneHandler>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

app.UseObservability();

// Fora de Development: página de erro genérica (sem stack trace) e HSTS.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Aluno sem celular confirmado vai para a confirmação antes de usar a área do aluno (D-059).
app.UseStudentPhoneGate();
app.UseAntiforgery();

// Toda rota exige login por padrão (FallbackPolicy). As públicas são marcadas com AllowAnonymous.
// O writer padrão responde só o status (Healthy/Degraded/Unhealthy), sem detalhes de exceção.
app.MapHealthChecks("/health").AllowAnonymous();

app.MapStaticAssets().AllowAnonymous();
app.MapLogout();
app.MapWhatsAppWebhook();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .Add(endpoint =>
    {
        // Endpoint do framework que só repassa um redirecionamento protegido durante a navegação aprimorada.
        // Os demais endpoints do Blazor (/_blazor) continuam exigindo login: não há página interativa pública.
        if (endpoint is RouteEndpointBuilder { RoutePattern.RawText: "/_framework/opaque-redirect" })
        {
            endpoint.Metadata.Add(new AllowAnonymousAttribute());
        }
    });

app.Run();
return 0;
