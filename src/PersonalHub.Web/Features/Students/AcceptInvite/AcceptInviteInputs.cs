namespace PersonalHub.Web.Features.Students.AcceptInvite;

/// <summary>Formulário do aluno sem conta: cria a conta e aceita o convite.</summary>
public sealed class NewAccountInput
{
    public string? Name { get; set; }
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }
    public bool AcceptTerms { get; set; }

    /// <summary>Prova protegida do código do WhatsApp (<see cref="InvitePhoneProof"/>), em campo oculto.</summary>
    public string? PhoneProof { get; set; }
}

/// <summary>Formulário de quem já tem conta (aluno de outro personal): só a senha e o aceite dos termos.</summary>
public sealed class ExistingAccountInput
{
    public string? Password { get; set; }
    public bool AcceptTerms { get; set; }

    /// <summary>Prova protegida do código do WhatsApp (<see cref="InvitePhoneProof"/>), em campo oculto.</summary>
    public string? PhoneProof { get; set; }
}
