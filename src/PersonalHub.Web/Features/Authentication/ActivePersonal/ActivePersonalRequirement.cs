using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Authentication.ActivePersonal;

public sealed class ActivePersonalRequirement : IAuthorizationRequirement;

/// <summary>
/// Confere no banco se o perfil do personal logado está ativo. A role no cookie só é revalidada a cada 30 minutos;
/// sem esta consulta, um personal desativado continuaria acessando a área até lá.
/// </summary>
public sealed class ActivePersonalHandler(AppDbContext db) : AuthorizationHandler<ActivePersonalRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ActivePersonalRequirement requirement)
    {
        // Quem não é personal já é barrado pela role da policy e volta para a própria página inicial.
        if (!context.User.IsInRole(AppRoles.Personal) || CurrentUser.IdOf(context.User) is not { } userId)
        {
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (await db.PersonalProfiles.AnyAsync(p => p.UserId == userId && p.IsActive, cancellationToken))
        {
            context.Succeed(requirement);
        }
        else
        {
            // O motivo identifica o caso para o InactivePersonalResultHandler encerrar a sessão.
            context.Fail(new AuthorizationFailureReason(this, "Perfil de personal inativo."));
        }
    }
}
