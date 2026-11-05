namespace PersonalHub.Web.Domain.Students;

/// <summary>Situação do convite mais recente, como o personal a vê nas telas de alunos.</summary>
public enum InviteStatus
{
    /// <summary>Convite em aberto e dentro da validade.</summary>
    Pending,
    Expired,

    /// <summary>Revogado (cancelado, trocado por um novo link, e-mail alterado ou vínculo inativado) ou inexistente.</summary>
    Cancelled,
    Used
}

public static class InviteStatusRules
{
    public static InviteStatus Of(DateTime? expiresAtUtc, DateTime? usedAtUtc, DateTime? revokedAtUtc, DateTime nowUtc)
    {
        if (expiresAtUtc is null || revokedAtUtc is not null)
        {
            return InviteStatus.Cancelled;
        }

        if (usedAtUtc is not null)
        {
            return InviteStatus.Used;
        }

        return nowUtc >= expiresAtUtc ? InviteStatus.Expired : InviteStatus.Pending;
    }
}
