using System.Security.Claims;

namespace PersonalHub.Web.Features.Authentication;

public static class CurrentUser
{
    /// <summary>Id do usuário autenticado, ou nulo. Serve para logs: nunca registrar e-mail ou nome.</summary>
    public static Guid? IdOf(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
