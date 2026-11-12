using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.PhoneVerification;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.PhoneVerification;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.PasswordReset;

public sealed record ResetRequestResult(bool Accepted, string? Error = null, string? ResetToken = null);

public enum ResetPasswordOutcome
{
    Succeeded,

    /// <summary>O valor protegido da URL não vale (adulterado ou mais de 30 minutos): volta para "Esqueci minha senha".</summary>
    LinkInvalid,

    /// <summary>Senha ou confirmação inválidas; o código não foi conferido nem consumido.</summary>
    Invalid,

    WrongCode,
    Locked,
    Expired
}

public sealed record ResetPasswordResult(
    ResetPasswordOutcome Outcome,
    IReadOnlyDictionary<string, string> Errors,
    int RemainingAttempts = 0,
    DateTime? LockedUntilUtc = null);

/// <summary>Formulário de /nova-senha.</summary>
public sealed class NewPasswordInput
{
    public string? Code { get; set; }
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }
}

/// <summary>
/// Recuperação de senha por WhatsApp (D-058). O pedido responde sempre igual, exista ou não a conta, e o código vai ao
/// celular confirmado da conta (ou ao celular do perfil do personal). Com o código certo, o token de troca do Identity é
/// gerado no servidor e usado na hora, o security stamp muda (as outras sessões caem) e o bloqueio por tentativas de
/// login é zerado. Logs e auditoria nunca levam e-mail, celular ou código.
/// </summary>
public sealed partial class PasswordResetHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    PhoneCodeService codes,
    IDataProtectionProvider dataProtection,
    TimeProvider clock,
    ILogger<PasswordResetHandler> logger)
{
    public const int EmailMaxLength = 256;

    /// <summary>Validade do valor que leva o e-mail de /esqueci-senha para /nova-senha.</summary>
    public static readonly TimeSpan LinkValidity = TimeSpan.FromMinutes(30);

    private readonly IDataProtector _emails = dataProtection.CreateProtector("PersonalHub.PasswordReset.Email.v1");

    public async Task<ResetRequestResult> RequestAsync(string? email, string? correlationId, CancellationToken cancellationToken)
    {
        var normalized = Normalize(email);
        if (normalized is null)
        {
            return new ResetRequestResult(false, string.IsNullOrWhiteSpace(email)
                ? "Informe seu e-mail."
                : "Informe um e-mail válido, como voce@exemplo.com.");
        }

        var target = await TargetAsync(normalized, cancellationToken);
        if (target is null)
        {
            LogNoTarget(logger);
        }
        else
        {
            var sent = await codes.SendAsync(
                new PhoneCodeScope(PhoneCodePurpose.PasswordReset, target.Value.Phone, UserId: target.Value.User.Id),
                target.Value.User.Id, correlationId, cancellationToken);
            LogRequested(logger, target.Value.User.Id, sent.Outcome);
        }

        // A mesma resposta nos dois casos: o valor protegido é gerado mesmo para e-mail sem conta.
        return new ResetRequestResult(true, ResetToken: ProtectEmail(normalized));
    }

    public async Task<ResetPasswordResult> ResetAsync(
        string? resetToken, NewPasswordInput input, string? correlationId, CancellationToken cancellationToken)
    {
        var email = UnprotectEmail(resetToken);
        if (email is null)
        {
            return Result(ResetPasswordOutcome.LinkInvalid);
        }

        var target = await TargetAsync(email, cancellationToken);
        var errors = await ValidatePasswordAsync(target?.User ?? new ApplicationUser { UserName = email, Email = email }, input);
        if (PhoneCodeRules.Normalize(input.Code) is null)
        {
            errors[nameof(NewPasswordInput.Code)] = PhoneCodeDisplay.Invalid;
        }

        if (errors.Count > 0)
        {
            return new ResetPasswordResult(ResetPasswordOutcome.Invalid, errors);
        }

        if (target is null)
        {
            // Sem conta ou sem celular: nunca houve código para este e-mail.
            return Result(ResetPasswordOutcome.Expired);
        }

        var (user, phone) = target.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var verify = await codes.VerifyAsync(new PhoneCodeScope(PhoneCodePurpose.PasswordReset, phone, UserId: user.Id),
            input.Code, correlationId, cancellationToken);
        if (verify.Outcome != PhoneCodeVerifyOutcome.Verified)
        {
            // Grava a tentativa errada (ou o bloqueio) antes de responder.
            await transaction.CommitAsync(cancellationToken);
            return verify.Outcome switch
            {
                PhoneCodeVerifyOutcome.Wrong => new ResetPasswordResult(ResetPasswordOutcome.WrongCode, Empty,
                    verify.RemainingAttempts),
                PhoneCodeVerifyOutcome.Locked => new ResetPasswordResult(ResetPasswordOutcome.Locked, Empty,
                    LockedUntilUtc: verify.LockedUntilUtc),
                _ => Result(ResetPasswordOutcome.Expired)
            };
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(user, token, input.Password!);
        if (!reset.Succeeded)
        {
            // Volta tudo, inclusive o consumo do código: o aluno corrige a senha e usa o mesmo código.
            await transaction.RollbackAsync(cancellationToken);
            return new ResetPasswordResult(ResetPasswordOutcome.Invalid, new Dictionary<string, string>
            {
                [nameof(NewPasswordInput.Password)] = reset.Errors.First().Description
            });
        }

        // ResetPasswordAsync já troca o stamp; a troca explícita deixa a intenção clara (derrubar as outras sessões).
        await userManager.UpdateSecurityStampAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);

        var now = clock.GetUtcNow().UtcDateTime;
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = user.Id,
            Action = "PasswordReset",
            EntityType = "User",
            EntityId = user.Id,
            OccurredAtUtc = now,
            CorrelationId = correlationId,
            Metadata = JsonSerializer.Serialize(new { via = "whatsapp" }),
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        LogReset(logger, user.Id);
        return Result(ResetPasswordOutcome.Succeeded);
    }

    /// <summary>O e-mail embutido no valor protegido, ou nulo se ele não vale mais.</summary>
    public string? UnprotectEmail(string? resetToken)
    {
        if (string.IsNullOrEmpty(resetToken) || resetToken.Length > 1000)
        {
            return null;
        }

        try
        {
            var parts = _emails.Unprotect(resetToken).Split('\n');
            return parts.Length == 2
                   && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                   && clock.GetUtcNow().UtcDateTime.Ticks < ticks
                ? parts[1]
                : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private string ProtectEmail(string email) =>
        _emails.Protect((clock.GetUtcNow().UtcDateTime + LinkValidity).Ticks.ToString(CultureInfo.InvariantCulture) + "\n" + email);

    /// <summary>A conta e o celular de destino; nulo se não há conta ou celular confirmado.</summary>
    private async Task<(ApplicationUser User, string Phone)?> TargetAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return null;
        }

        var phone = user.PhoneNumberConfirmed ? BrazilianPhone.ToE164(user.PhoneNumber) : null;
        phone ??= BrazilianPhone.ToE164(await db.PersonalProfiles.Where(p => p.UserId == user.Id)
            .Select(p => p.PhoneNumber).SingleOrDefaultAsync(cancellationToken));
        return phone is null ? null : (user, phone);
    }

    private async Task<Dictionary<string, string>> ValidatePasswordAsync(ApplicationUser user, NewPasswordInput input)
    {
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(input.Password))
        {
            errors[nameof(NewPasswordInput.Password)] = "Crie uma senha.";
        }
        else
        {
            foreach (var validator in userManager.PasswordValidators)
            {
                var result = await validator.ValidateAsync(userManager, user, input.Password);
                if (!result.Succeeded)
                {
                    errors[nameof(NewPasswordInput.Password)] = result.Errors.First().Description;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(input.ConfirmPassword))
        {
            errors[nameof(NewPasswordInput.ConfirmPassword)] = "Confirme a senha.";
        }
        else if (!string.Equals(input.Password, input.ConfirmPassword, StringComparison.Ordinal))
        {
            errors[nameof(NewPasswordInput.ConfirmPassword)] = "As senhas não são iguais.";
        }

        return errors;
    }

    private static string? Normalize(string? email)
    {
        var text = email?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > EmailMaxLength || !new EmailAddressAttribute().IsValid(text)
            ? null
            : text.ToLowerInvariant();
    }

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    private static ResetPasswordResult Result(ResetPasswordOutcome outcome) => new(outcome, Empty);

    [LoggerMessage(EventId = 2200, Level = LogLevel.Information,
        Message = "Recuperação de senha pedida para e-mail sem conta ou sem celular confirmado")]
    private static partial void LogNoTarget(ILogger logger);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Information,
        Message = "Recuperação de senha pedida pelo usuário {UserId}: {Outcome}")]
    private static partial void LogRequested(ILogger logger, Guid userId, PhoneCodeSendOutcome outcome);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Information, Message = "Senha trocada por código do WhatsApp (usuário {UserId})")]
    private static partial void LogReset(ILogger logger, Guid userId);
}

public static class PasswordResetServiceCollectionExtensions
{
    public static IServiceCollection AddPasswordReset(this IServiceCollection services) =>
        services.AddScoped<PasswordResetHandler>();
}
