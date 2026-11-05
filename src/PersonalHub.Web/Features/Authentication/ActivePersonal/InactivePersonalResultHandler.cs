using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Features.Authentication.ActivePersonal;

/// <summary>
/// Personal desativado com cookie ainda válido: encerra a sessão e volta para /entrar, onde o login já recusa
/// contas inativas. Os demais casos seguem o comportamento padrão (anônimo vai para o login; outro perfil, para a
/// própria página inicial).
/// </summary>
public sealed partial class InactivePersonalResultHandler(ILogger<InactivePersonalResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var inactivePersonal = authorizeResult.Forbidden &&
            authorizeResult.AuthorizationFailure?.FailureReasons.Any(r => r.Handler is ActivePersonalHandler) == true;
        if (!inactivePersonal)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var userId = CurrentUser.IdOf(context.User);
        await context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync();
        LogInactivePersonalSignedOut(logger, userId);

        context.Response.Redirect("/entrar");
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning,
        Message = "Sessão encerrada: personal {UserId} está inativo")]
    private static partial void LogInactivePersonalSignedOut(ILogger logger, Guid? userId);
}
