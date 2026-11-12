using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.PhoneVerification;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.PhoneVerification.ConfirmPhone;

/// <summary>Tela de confirmação. <see cref="AlreadyConfirmed"/>: o cookie só estava desatualizado.</summary>
public sealed record ConfirmPhoneView(
    bool AlreadyConfirmed,
    bool HasPhone = false,
    string? MaskedPhone = null,
    string? PersonalFirstName = null,
    PhoneCodeStatus? Code = null);

/// <summary>
/// Aluno que aceitou o convite antes da Fase 6 confirma o celular (D-059): o código vai ao celular do vínculo mais
/// recente (o que o personal cadastrou), pelo mesmo fluxo do convite. Confirmado, o celular vira o da conta e o
/// cookie é renovado com o claim de celular confirmado.
/// </summary>
public sealed partial class ConfirmPhoneHandler(
    AppDbContext db,
    PhoneCodeService codes,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    TimeProvider clock,
    ILogger<ConfirmPhoneHandler> logger)
{
    private sealed record Target(string Phone, string PersonalName);

    public async Task<ConfirmPhoneView> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await IsConfirmedAsync(userId, cancellationToken))
        {
            return new ConfirmPhoneView(AlreadyConfirmed: true);
        }

        var target = await TargetAsync(userId, cancellationToken);
        if (target is null)
        {
            return new ConfirmPhoneView(AlreadyConfirmed: false);
        }

        var status = await codes.GetStatusAsync(Scope(userId, target), cancellationToken);
        return new ConfirmPhoneView(false, true, PhoneCodeRules.Mask(target.Phone),
            PhoneCodeDisplay.FirstName(target.PersonalName), status);
    }

    public async Task<PhoneCodeSendResult?> SendAsync(Guid userId, string? correlationId, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(userId, cancellationToken);
        return target is null ? null : await codes.SendAsync(Scope(userId, target), userId, correlationId, cancellationToken);
    }

    /// <summary>Confere o código e, se certo, grava o celular confirmado e renova o cookie, tudo na mesma transação.</summary>
    public async Task<PhoneCodeVerifyResult?> VerifyAsync(
        Guid userId, string? code, string? correlationId, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(userId, cancellationToken);
        if (target is null)
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await codes.VerifyAsync(Scope(userId, target), code, correlationId, cancellationToken);
        if (result.Outcome != PhoneCodeVerifyOutcome.Verified)
        {
            // Grava a tentativa errada (ou o bloqueio).
            await transaction.CommitAsync(cancellationToken);
            return result;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.PhoneNumber, target.Phone)
                .SetProperty(u => u.PhoneNumberConfirmed, true), cancellationToken);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = userId,
            Action = "PhoneConfirmed",
            EntityType = "User",
            EntityId = userId,
            OccurredAtUtc = now,
            CorrelationId = correlationId,
            Metadata = JsonSerializer.Serialize(new { purpose = PhoneCodePurpose.PhoneConfirmation.ToString() }),
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await RefreshCookieAsync(userId);
        LogConfirmed(logger, userId);
        return result;
    }

    /// <summary>Recria o cookie com o claim atual (por exemplo, confirmado em outro aparelho).</summary>
    public async Task RefreshCookieAsync(Guid userId)
    {
        db.ChangeTracker.Clear();
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is not null)
        {
            await signInManager.RefreshSignInAsync(user);
        }
    }

    private Task<bool> IsConfirmedAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == userId && u.PhoneNumberConfirmed && u.PhoneNumber != null, cancellationToken);

    /// <summary>Celular do vínculo mais recente do aluno (o último aceito; empate pelo cadastro).</summary>
    private Task<Target?> TargetAsync(Guid userId, CancellationToken cancellationToken) =>
        (from s in db.PersonalStudents
         join p in db.PersonalProfiles on s.PersonalId equals p.Id
         where s.StudentUserId == userId
         orderby s.ActivatedAtUtc descending, s.CreatedAtUtc descending
         select new Target(s.StudentPhone, p.DisplayName))
        .AsNoTracking()
        .FirstOrDefaultAsync(cancellationToken);

    private static PhoneCodeScope Scope(Guid userId, Target target) =>
        new(PhoneCodePurpose.PhoneConfirmation, target.Phone, UserId: userId);

    [LoggerMessage(EventId = 7320, Level = LogLevel.Information, Message = "Celular confirmado pelo usuário {UserId}")]
    private static partial void LogConfirmed(ILogger logger, Guid userId);
}
