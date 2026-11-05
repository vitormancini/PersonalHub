using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PersonalHub.IntegrationTests.Infrastructure;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.IntegrationTests.Authentication;

/// <summary>Autorização por padrão: só as rotas desta lista respondem sem login.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuthorizationPolicyTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly string[] AnonymousRoutes =
    [
        "/",
        "/entrar",
        "/sair",
        "/convite/{Token}",
        "/termos",
        "/privacidade",
        "/Error",
        "/not-found",
        "/health",
        "/_framework/opaque-redirect"
    ];

    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;

    public Task InitializeAsync()
    {
        _factory = TestApp.Create(fixture.ConnectionString);
        _client = _factory.CreateBrowser();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public void FallbackPolicy_RequiresAuthenticatedUser()
    {
        var options = _factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(options.FallbackPolicy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void OnlyAllowlistedRoutes_AreAnonymous()
    {
        var anonymous = RouteEndpoints()
            .Where(e => e.Metadata.GetMetadata<StaticAssetDescriptor>() is null)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText)
            .Distinct()
            .Order();

        Assert.Equal(AnonymousRoutes.Order(), anonymous);
    }

    [Fact]
    public async Task EveryOtherEndpoint_RequiresLogin_EvenWithoutAuthorizeAttribute()
    {
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var protectedEndpoints = RouteEndpoints().Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null).ToList();
        Assert.NotEmpty(protectedEndpoints);

        foreach (var endpoint in protectedEndpoints)
        {
            // Mesma combinação que o AuthorizationMiddleware faz; sem atributo, vale a FallbackPolicy.
            var policy = await AuthorizationPolicy.CombineAsync(provider,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());

            Assert.True(policy is not null &&
                        policy.Requirements.Any(r => r is DenyAnonymousAuthorizationRequirement or RolesAuthorizationRequirement),
                $"{endpoint.RoutePattern.RawText} não exige login.");
        }
    }

    [Fact]
    public void PersonalPages_UseActivePersonalPolicy()
    {
        var personalPages = RouteEndpoints()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/personal", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(personalPages);

        Assert.All(personalPages, endpoint =>
        {
            var authorize = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Equal(AppPolicies.ActivePersonal, authorize.Policy);
            Assert.Null(authorize.Roles);
        });
    }

    [Theory]
    [InlineData("/_blazor/initializers/")] // endpoint do framework, sem [Authorize]
    [InlineData("/rota-que-nao-existe")]   // sem endpoint também vale a FallbackPolicy
    public async Task RouteWithoutAuthorizeMetadata_RedirectsAnonymousToLogin(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.StartsWith("/entrar?ReturnUrl=", response.RedirectTarget());
    }

    [Theory]
    [InlineData("/entrar")]
    [InlineData("/termos")]
    [InlineData("/privacidade")]
    [InlineData("/convite/token-invalido")]
    [InlineData("/Error")]
    [InlineData("/not-found")]
    [InlineData("/health")]
    [InlineData("/app.css")]
    [InlineData("/css/personalhub.css")]
    [InlineData("/js/personalhub.js")]
    [InlineData("/favicon.png")]
    public async Task PublicRoutes_LoadAnonymously(string path)
    {
        var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BlazorScript_IsNotBehindLogin()
    {
        // Fora de Development o host de teste não resolve os static web assets do framework (o arquivo dá erro),
        // então aqui só se confere que a autorização deixa passar.
        var response = await _client.GetAsync("/_framework/blazor.web.js");

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    private List<RouteEndpoint> RouteEndpoints() =>
        _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
}
