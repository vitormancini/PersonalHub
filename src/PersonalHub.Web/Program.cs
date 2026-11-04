using PersonalHub.Web.Components;
using PersonalHub.Web.Features.Authentication.Login;
using PersonalHub.Web.Features.Authentication.Logout;
using PersonalHub.Web.Features.Authentication.ProvisionPersonal;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Observability;
using PersonalHub.Web.Infrastructure.Persistence;

// Comando administrativo (D-027): roda e encerra sem iniciar o servidor web.
if (ProvisionPersonalCommand.IsInvoked(args))
{
    return await ProvisionPersonalCommand.RunAsync(args);
}

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddPersistence();
builder.Services.AddAppIdentityCore();
builder.Services.AddAppAuthentication();
builder.Services.AddLoginRateLimiting();
builder.Services.AddScoped<LoginHandler>();

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
app.UseAntiforgery();

// O writer padrão responde só o status (Healthy/Degraded/Unhealthy), sem detalhes de exceção.
app.MapHealthChecks("/health");

app.MapStaticAssets();
app.MapLogout();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;
