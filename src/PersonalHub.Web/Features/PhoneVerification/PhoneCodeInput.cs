namespace PersonalHub.Web.Features.PhoneVerification;

/// <summary>Campo do código de 6 números. A validação é do serviço (<see cref="Domain.PhoneVerification.PhoneCodeRules"/>).</summary>
public sealed class PhoneCodeInput
{
    public string? Value { get; set; }
}
