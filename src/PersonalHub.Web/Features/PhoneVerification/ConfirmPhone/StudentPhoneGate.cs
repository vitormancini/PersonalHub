using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Features.PhoneVerification.ConfirmPhone;

/// <summary>
/// Aluno sem celular confirmado (aceitou o convite antes da Fase 6) só usa a área do aluno depois de confirmar o
/// celular (D-059). Lê o claim do cookie (<see cref="AppClaimsPrincipalFactory"/>); a página de confirmação confere
/// o banco de novo e atualiza o cookie. Contas que também são personal seguem para a área do personal normalmente.
/// </summary>
public static class StudentPhoneGate
{
    public const string ConfirmPath = "/aluno/confirmar-celular";

    public static IApplicationBuilder UseStudentPhoneGate(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (MustConfirm(context))
            {
                context.Response.Redirect(ConfirmPath);
                return;
            }

            await next(context);
        });

    public static bool MustConfirm(HttpContext context)
    {
        var path = context.Request.Path;
        var user = context.User;
        return path.StartsWithSegments("/aluno", StringComparison.OrdinalIgnoreCase)
               && !path.StartsWithSegments(ConfirmPath, StringComparison.OrdinalIgnoreCase)
               && user.Identity?.IsAuthenticated == true
               && user.IsInRole(AppRoles.Student)
               && !user.IsInRole(AppRoles.Personal)
               && !user.HasClaim(AppClaimsPrincipalFactory.PhoneConfirmedClaim, "true");
    }
}
