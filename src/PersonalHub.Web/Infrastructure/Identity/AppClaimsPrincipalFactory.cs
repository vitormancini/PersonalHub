using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace PersonalHub.Web.Infrastructure.Identity;

/// <summary>
/// Acrescenta ao cookie de login se o celular da conta está confirmado (D-059). Assim a exigência de confirmar o
/// celular não consulta o banco a cada requisição; o cookie é recriado no login, ao confirmar e a cada validação do
/// security stamp.
/// </summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    public const string PhoneConfirmedClaim = "ph:phone_confirmed";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.PhoneNumberConfirmed)
        {
            identity.AddClaim(new Claim(PhoneConfirmedClaim, "true"));
        }

        return identity;
    }
}
