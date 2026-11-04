using System.Security.Claims;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Features.Authentication;

/// <summary>Página inicial de cada perfil. Usada após o login e para quem abre a área de outro perfil.</summary>
public static class UserHome
{
    public const string Personal = "/personal";
    public const string Student = "/aluno";

    /// <summary>Personal tem prioridade se a conta tiver os dois perfis. Nulo quando não há perfil conhecido.</summary>
    public static string? ForRoles(IEnumerable<string> roles)
    {
        var set = roles as ICollection<string> ?? roles.ToList();
        if (set.Contains(AppRoles.Personal))
        {
            return Personal;
        }

        return set.Contains(AppRoles.Student) ? Student : null;
    }

    public static string? For(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (user.IsInRole(AppRoles.Personal))
        {
            return Personal;
        }

        return user.IsInRole(AppRoles.Student) ? Student : null;
    }
}
