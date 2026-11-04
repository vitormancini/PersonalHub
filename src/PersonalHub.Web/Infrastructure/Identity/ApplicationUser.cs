using Microsoft.AspNetCore.Identity;

namespace PersonalHub.Web.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Nome que o próprio aluno informou ao aceitar o convite (D-032). Nulo para personais, cujo nome fica
    /// em <c>PersonalProfile</c>. Não substitui o nome que cada personal cadastrou no vínculo.
    /// </summary>
    public string? FullName { get; set; }
}
