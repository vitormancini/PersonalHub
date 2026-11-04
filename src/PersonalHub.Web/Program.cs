using PersonalHub.Web.Components;
using PersonalHub.Web.Infrastructure.Observability;
using PersonalHub.Web.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddPersistence();

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

app.UseAntiforgery();

// O writer padrão responde só o status (Healthy/Degraded/Unhealthy), sem detalhes de exceção.
app.MapHealthChecks("/health");

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
