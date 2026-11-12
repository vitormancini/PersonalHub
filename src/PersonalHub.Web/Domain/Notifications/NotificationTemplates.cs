using System.Text;

namespace PersonalHub.Web.Domain.Notifications;

/// <summary>
/// Templates aprovados na Meta (WHATSAPP_TEMPLATES.md). O nome do template é o <c>EventType</c> da outbox. Nenhum texto
/// livre digitado por usuário (motivo, observação, mensagem) entra numa variável (D-060).
/// </summary>
public static class NotificationTemplates
{
    public const string Language = "pt_BR";

    public const string Code = "ph_codigo";
    public const string LessonCreated = "ph_nova_aula";
    public const string LessonDeleted = "ph_aula_excluida";
    public const string StatementIssued = "ph_demonstrativo_emitido";
    public const string PaymentConfirmed = "ph_pagamento_confirmado";
    public const string PaymentRejected = "ph_pagamento_nao_recebido";
    public const string LessonApproved = "ph_aula_confirmada";
    public const string LessonRejected = "ph_aula_recusada";
    public const string PaymentReported = "ph_pagamento_informado";

    public const int VariableMaxLength = 60;

    /// <summary>
    /// Variável de template: sem quebra de linha nem tabulação (a Meta recusa), sem mais de um espaço seguido e com
    /// tamanho limitado.
    /// </summary>
    public static string Variable(string? value)
    {
        var text = new StringBuilder(value?.Length ?? 0);
        var lastWasSpace = true;
        foreach (var c in value ?? string.Empty)
        {
            var space = char.IsWhiteSpace(c) || char.IsControl(c);
            if (space)
            {
                if (!lastWasSpace)
                {
                    text.Append(' ');
                }
            }
            else
            {
                text.Append(c);
            }

            lastWasSpace = space;
        }

        var result = text.ToString().Trim();
        if (result.Length > VariableMaxLength)
        {
            result = result[..(VariableMaxLength - 1)].TrimEnd() + "…";
        }

        return result.Length == 0 ? "-" : result;
    }

    /// <summary>Primeiro nome, para a saudação ("Olá, Mariana!").</summary>
    public static string FirstName(string? fullName)
    {
        var name = Variable(fullName);
        var space = name.IndexOf(' ');
        return space > 0 ? name[..space] : name;
    }
}
