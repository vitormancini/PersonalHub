using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Features.Authentication.Logout;

public static partial class LogoutEndpoint
{
    public const string Path = "/sair";

    /// <summary>
    /// POST /sair: valida o antiforgery, apaga o cookie de login e volta para /entrar. Não existe GET, para que
    /// um link ou imagem em outro site não consiga deslogar o usuário.
    /// </summary>
    public static IEndpointRouteBuilder MapLogout(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(Path, async (
                HttpContext http,
                IAntiforgery antiforgery,
                SignInManager<ApplicationUser> signInManager,
                ILoggerFactory loggerFactory) =>
            {
                // Sem parâmetro de formulário, o minimal API não confere o token sozinho.
                if (!await antiforgery.IsRequestValidAsync(http))
                {
                    return Results.BadRequest();
                }

                var userId = CurrentUser.IdOf(http.User);
                await signInManager.SignOutAsync();
                if (userId is not null)
                {
                    LogSignedOut(loggerFactory.CreateLogger(typeof(LogoutEndpoint).FullName!), userId.Value);
                }

                return Results.LocalRedirect("/entrar");
            })
            // Sair com a sessão já expirada só leva de volta para /entrar; o antiforgery continua exigido.
            .AllowAnonymous();

        return endpoints;
    }

    [LoggerMessage(EventId = 2020, Level = LogLevel.Information, Message = "Logout do usuário {UserId}")]
    private static partial void LogSignedOut(ILogger logger, Guid userId);
}
