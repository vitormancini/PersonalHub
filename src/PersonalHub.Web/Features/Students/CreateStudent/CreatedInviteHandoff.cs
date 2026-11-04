using Microsoft.AspNetCore.DataProtection;

namespace PersonalHub.Web.Features.Students.CreateStudent;

/// <summary>
/// Leva o token puro do POST de cadastro até a página "Convite do aluno" (PRG) sem gravá-lo no banco nem na
/// URL. Funciona como um TempData de leitura única: cookie cifrado pelo Data Protection, válido por 10 minutos,
/// ligado ao personal e ao vínculo, e apagado assim que lido. Recarregar a página não mostra o link de novo.
/// </summary>
public sealed class CreatedInviteHandoff(IDataProtectionProvider dataProtection, TimeProvider clock)
{
    public const string CookieName = "__Host-PersonalHub.Invite";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ITimeLimitedDataProtector _protector =
        dataProtection.CreateProtector("PersonalHub.Students.CreatedInvite").ToTimeLimitedDataProtector();

    public void Store(HttpContext http, Guid personalUserId, Guid personalStudentId, string token)
    {
        var payload = $"{personalUserId:N}|{personalStudentId:N}|{token}";
        var expiration = clock.GetUtcNow() + Lifetime;
        http.Response.Cookies.Append(CookieName, _protector.Protect(payload, expiration), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = Lifetime,
            IsEssential = true
        });
    }

    /// <summary>Lê e apaga o cookie. Nulo se não existir, expirou, foi adulterado ou é de outro vínculo/usuário.</summary>
    public string? Take(HttpContext http, Guid personalUserId, Guid personalStudentId)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var protectedValue))
        {
            return null;
        }

        http.Response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, Path = "/", SameSite = SameSiteMode.Strict });

        string payload;
        try
        {
            payload = _protector.Unprotect(protectedValue, out _);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }

        var parts = payload.Split('|');
        return parts.Length == 3
               && parts[0] == personalUserId.ToString("N")
               && parts[1] == personalStudentId.ToString("N")
            ? parts[2]
            : null;
    }
}
