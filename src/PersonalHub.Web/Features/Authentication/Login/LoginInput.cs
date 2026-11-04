using System.ComponentModel.DataAnnotations;

namespace PersonalHub.Web.Features.Authentication.Login;

/// <summary>Campos do formulário de login. Validados no servidor; o browser só ajuda (teclado de e-mail etc.).</summary>
public sealed class LoginInput
{
    [Required(ErrorMessage = "Informe seu e-mail.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido, como voce@exemplo.com.")]
    [StringLength(256, ErrorMessage = "Informe um e-mail válido, como voce@exemplo.com.")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Informe sua senha.")]
    [StringLength(512, ErrorMessage = "Senha longa demais.")]
    public string? Password { get; set; }

    public bool RememberMe { get; set; }
}
