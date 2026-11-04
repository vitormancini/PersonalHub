namespace PersonalHub.Web.Domain.Students;

public enum InviteState
{
    Valid,
    Expired,
    Used,

    /// <summary>Token desconhecido, malformado ou revogado, vínculo inativo ou personal inativo.</summary>
    NotFound
}

public static class InviteRules
{
    /// <summary>
    /// Situação do convite. Revogado e vínculo inativo contam como inexistente, para não revelar nada ao
    /// portador do link. Vínculo já ativo (aceito por outro convite) conta como usado.
    /// </summary>
    public static InviteState Evaluate(
        DateTime expiresAtUtc,
        DateTime? usedAtUtc,
        DateTime? revokedAtUtc,
        PersonalStudentStatus linkStatus,
        bool personalActive,
        DateTime nowUtc)
    {
        if (revokedAtUtc is not null || linkStatus == PersonalStudentStatus.Inactive || !personalActive)
        {
            return InviteState.NotFound;
        }

        if (usedAtUtc is not null || linkStatus == PersonalStudentStatus.Active)
        {
            return InviteState.Used;
        }

        return nowUtc >= expiresAtUtc ? InviteState.Expired : InviteState.Valid;
    }
}
