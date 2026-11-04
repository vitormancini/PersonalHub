namespace PersonalHub.Web.Features.Students.CreateStudent;

/// <summary>
/// Link "clique para conversar" do WhatsApp (D-031): abre o WhatsApp do próprio personal com a mensagem
/// pronta. Nada é enviado pelo PersonalHub; o personal decide enviar.
/// </summary>
public static class WhatsAppInvite
{
    public static string FirstName(string fullName)
    {
        var trimmed = fullName.Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }

    public static string Message(string studentName, string personalName, string inviteUrl) =>
        $"Olá, {FirstName(studentName)}! {personalName} convidou você para o PersonalHub. " +
        $"Crie sua senha por este link (vale por 7 dias): {inviteUrl}";

    /// <summary>https://wa.me/5511999998888?text=... (número E.164 sem o "+").</summary>
    public static string Url(string phoneE164, string message) =>
        $"https://wa.me/{phoneE164.TrimStart('+')}?text={Uri.EscapeDataString(message)}";
}
