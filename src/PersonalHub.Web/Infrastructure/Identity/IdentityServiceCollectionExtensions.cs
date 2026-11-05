using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using PersonalHub.Web.Features.Authentication;
using PersonalHub.Web.Features.Authentication.ActivePersonal;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Infrastructure.Identity;

public static partial class IdentityServiceCollectionExtensions
{
    public const int LockoutMaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan PersistentLoginDuration = TimeSpan.FromDays(14);

    /// <summary>
    /// Núcleo do Identity: usuários, roles, stores e políticas de senha e bloqueio. Usado pela aplicação web
    /// e pelo comando de provisionamento, que não precisa de cookies.
    /// </summary>
    public static IServiceCollection AddAppIdentityCore(this IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                // AllowedForNewUsers liga o bloqueio para toda conta criada pelo UserManager.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = LockoutMaxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;

                // Personais são provisionados pelo dono do produto; alunos validam o e-mail pelo convite (D-016).
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddErrorDescriber<PortugueseIdentityErrorDescriber>();

        return services;
    }

    /// <summary>Login por cookie do Identity, autorização e estado de autenticação para os componentes.</summary>
    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        // Complementa o núcleo registrado em AddAppIdentityCore sem registrar os validadores de novo.
        new IdentityBuilder(typeof(ApplicationUser), typeof(IdentityRole<Guid>), services)
            .AddSignInManager();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            // __Host-: o browser só aceita o cookie com Secure, Path=/ e sem Domain.
            options.Cookie.Name = "__Host-PersonalHub.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;

            // Vale para "Manter conectado". Sem a opção, o cookie é de sessão do browser.
            options.ExpireTimeSpan = PersistentLoginDuration;
            options.SlidingExpiration = true;

            options.LoginPath = "/entrar";
            options.LogoutPath = "/sair";
            options.Events.OnRedirectToAccessDenied = RedirectToOwnHomeAsync;
        });

        services.AddAuthorizationBuilder()
            // Autorização por padrão: endpoint sem atributo exige login. Páginas públicas usam [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AppPolicies.ActivePersonal, policy => policy
                .RequireRole(AppRoles.Personal)
                .AddRequirements(new ActivePersonalRequirement()));
        services.AddScoped<IAuthorizationHandler, ActivePersonalHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, InactivePersonalResultHandler>();
        services.AddCascadingAuthenticationState();

        return services;
    }

    /// <summary>
    /// Usuário autenticado tentando abrir a área de outro perfil (por exemplo, aluno em /personal):
    /// volta para a própria página inicial. Sem perfil conhecido, responde 403.
    /// </summary>
    private static Task RedirectToOwnHomeAsync(RedirectContext<CookieAuthenticationOptions> context)
    {
        var http = context.HttpContext;
        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PersonalHub.Web.Authorization");
        LogAccessDenied(logger, http.Request.Path.Value ?? "/", CurrentUser.IdOf(http.User));

        var home = UserHome.For(http.User);
        if (home is null)
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
        }
        else
        {
            http.Response.Redirect(home);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Warning,
        Message = "Acesso negado a {Path} para o usuário {UserId}")]
    private static partial void LogAccessDenied(ILogger logger, string path, Guid? userId);
}
