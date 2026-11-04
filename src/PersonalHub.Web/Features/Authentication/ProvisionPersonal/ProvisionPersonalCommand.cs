using System.Text;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Authentication.ProvisionPersonal;

public sealed record ProvisionPersonalArguments(string Email, string Name, string? Phone);

/// <summary>
/// <c>dotnet run --project src/PersonalHub.Web -- provision-personal --email x --name "Y" [--phone +55...]</c>
/// <para>
/// Lê a senha da entrada padrão sem eco (ou de um pipe, para automação), cria o personal e encerra sem subir o
/// servidor web. A senha nunca é aceita como argumento: ficaria no histórico do shell e na lista de processos.
/// </para>
/// </summary>
public static class ProvisionPersonalCommand
{
    public const string Name = "provision-personal";

    public const string Usage = """
        Uso: dotnet run --project src/PersonalHub.Web -- provision-personal --email <e-mail> --name "<nome>" [--phone <+55...>]

        A senha é pedida no terminal, sem aparecer na tela. Também pode vir por pipe (uma linha).
        Regras: mínimo de 8 caracteres, com letra maiúscula, letra minúscula e número.
        """;

    public static bool IsInvoked(string[] args) => args.Length > 0 && args[0] == Name;

    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryParse(args.Skip(1).ToArray(), out var arguments, out var error))
        {
            await Console.Error.WriteLineAsync(error);
            await Console.Error.WriteLineAsync(Usage);
            return 2;
        }

        var password = ReadPassword();
        if (password is null)
        {
            await Console.Error.WriteLineAsync("As senhas digitadas não conferem.");
            return 1;
        }

        // Mesma configuração da aplicação (appsettings, user secrets em Development, variáveis de ambiente),
        // mas só com persistência e Identity: o host web não é iniciado.
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        // Descarta as regras do appsettings (o SQL do EF em Development) e mostra só avisos e erros.
        builder.Services.PostConfigure<LoggerFilterOptions>(options =>
        {
            options.Rules.Clear();
            options.MinLevel = LogLevel.Warning;
        });
        builder.Services.AddPersistence();
        builder.Services.AddAppIdentityCore();
        builder.Services.AddScoped<ProvisionPersonalHandler>();

        await using var app = builder.Build();
        await using var scope = app.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ProvisionPersonalHandler>();

        var result = await handler.HandleAsync(
            new ProvisionPersonalRequest(arguments!.Email, arguments.Name, arguments.Phone, password),
            CancellationToken.None);

        if (!result.Succeeded)
        {
            await Console.Error.WriteLineAsync("Personal não criado:");
            foreach (var message in result.Errors)
            {
                await Console.Error.WriteLineAsync($"  - {message}");
            }

            return 1;
        }

        Console.WriteLine($"Personal criado. Id do usuário: {result.UserId}");
        return 0;
    }

    public static bool TryParse(string[] args, out ProvisionPersonalArguments? arguments, out string? error)
    {
        arguments = null;
        string? email = null, name = null, phone = null;

        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i];
            if (option is not ("--email" or "--name" or "--phone"))
            {
                error = $"Opção desconhecida: {option}";
                return false;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Falta o valor de {option}.";
                return false;
            }

            var value = args[++i];
            switch (option)
            {
                case "--email": email = value; break;
                case "--name": name = value; break;
                default: phone = value; break;
            }
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(name))
        {
            error = "Informe --email e --name.";
            return false;
        }

        arguments = new ProvisionPersonalArguments(email, name, phone);
        error = null;
        return true;
    }

    /// <summary>No terminal, pede a senha duas vezes sem eco; por pipe, lê uma linha. Nulo se não conferirem.</summary>
    private static string? ReadPassword()
    {
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine() ?? string.Empty;
        }

        var password = ReadHidden("Senha: ");
        var confirmation = ReadHidden("Confirme a senha: ");
        return password == confirmation ? password : null;
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
