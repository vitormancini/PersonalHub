namespace PersonalHub.Web.Domain.PhoneVerification;

public enum PhoneCodePurpose
{
    /// <summary>Aceite do convite (D-057): o código vai ao celular cadastrado pelo personal.</summary>
    InviteAcceptance,

    /// <summary>Aluno que aceitou antes da Fase 6 confirma o celular (D-059).</summary>
    PhoneConfirmation,

    /// <summary>Recuperação de senha (D-058).</summary>
    PasswordReset
}
