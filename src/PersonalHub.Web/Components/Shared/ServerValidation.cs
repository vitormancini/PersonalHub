using Microsoft.AspNetCore.Components.Forms;

namespace PersonalHub.Web.Components.Shared;

/// <summary>
/// Mostra no formulário os erros devolvidos por um handler (chave = nome da propriedade do modelo). Os campos
/// com erro ganham a classe "invalid" e aria-invalid, como na validação por DataAnnotations.
/// </summary>
public static class ServerValidation
{
    public static void Show(EditContext editContext, ValidationMessageStore store, IReadOnlyDictionary<string, string> errors)
    {
        store.Clear();
        foreach (var (field, message) in errors)
        {
            store.Add(editContext.Field(field), message);
        }

        editContext.NotifyValidationStateChanged();
    }
}
