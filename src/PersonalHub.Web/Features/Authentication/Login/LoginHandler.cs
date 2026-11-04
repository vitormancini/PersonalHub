using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Authentication.Login;

public sealed record LoginCommand(string Email, string Password, bool RememberMe);

public enum LoginOutcome
{
    Succeeded,

    /// <summary>Senha errada, e-mail desconhecido, personal inativo ou conta sem perfil: a mesma mensagem.</summary>
    InvalidCredentials,

    LockedOut
}

public sealed record LoginResult(LoginOutcome Outcome, string? HomeUrl = null);

/// <summary>
/// Confere as credenciais, aplica bloqueio por tentativas e só então emite o cookie. A resposta nunca revela
/// se o e-mail existe. Os logs levam o id do usuário (quando conhecido), nunca e-mail ou senha.
/// </summary>
public sealed partial class LoginHandler(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AppDbContext db,
    ILogger<LoginHandler> logger)
{
    public async Task<LoginResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(command.Email.Trim());
        if (user is null)
        {
            // Calcula um hash à toa para o tempo de resposta não denunciar que o e-mail não existe.
            userManager.PasswordHasher.HashPassword(new ApplicationUser(), command.Password);
            LogInvalidCredentials(logger, null);
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        // Conta a falha e bloqueia após o limite; zera o contador em caso de sucesso.
        var check = await signInManager.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            LogLockedOut(logger, user.Id);
            return new LoginResult(LoginOutcome.LockedOut);
        }

        if (!check.Succeeded)
        {
            LogInvalidCredentials(logger, user.Id);
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        var roles = await userManager.GetRolesAsync(user);
        var home = UserHome.ForRoles(roles);
        if (home is null)
        {
            LogWithoutRole(logger, user.Id);
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        if (roles.Contains(AppRoles.Personal) && !await IsActivePersonalAsync(user.Id, cancellationToken))
        {
            LogInactivePersonal(logger, user.Id);
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        await signInManager.SignInAsync(user, isPersistent: command.RememberMe);
        LogSucceeded(logger, user.Id);
        return new LoginResult(LoginOutcome.Succeeded, home);
    }

    private Task<bool> IsActivePersonalAsync(Guid userId, CancellationToken cancellationToken) =>
        db.PersonalProfiles.AnyAsync(p => p.UserId == userId && p.IsActive, cancellationToken);

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Login efetuado pelo usuário {UserId}")]
    private static partial void LogSucceeded(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Login recusado: credenciais inválidas (usuário {UserId})")]
    private static partial void LogInvalidCredentials(ILogger logger, Guid? userId);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Login recusado: conta bloqueada por excesso de tentativas (usuário {UserId})")]
    private static partial void LogLockedOut(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning,
        Message = "Login recusado: personal inativo (usuário {UserId})")]
    private static partial void LogInactivePersonal(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning,
        Message = "Login recusado: conta sem perfil Personal ou Student (usuário {UserId})")]
    private static partial void LogWithoutRole(ILogger logger, Guid userId);
}
