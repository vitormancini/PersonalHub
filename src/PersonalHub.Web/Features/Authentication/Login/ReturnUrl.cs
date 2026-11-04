namespace PersonalHub.Web.Features.Authentication.Login;

public static class ReturnUrl
{
    /// <summary>
    /// Aceita somente caminhos locais ("/personal", "/aluno?x=1"). Recusa URLs absolutas e as formas que o
    /// browser trata como outro host ("//site", "/\site", "/\t/site"), evitando open redirect.
    /// Mesma regra de <c>IUrlHelper.IsLocalUrl</c>, sem o prefixo "~/".
    /// </summary>
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            return false;
        }

        if (url.Length == 1)
        {
            return true;
        }

        if (url[1] is '/' or '\\')
        {
            return false;
        }

        foreach (var c in url)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }
}
