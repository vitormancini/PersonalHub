using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.PhoneVerification;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Features.PhoneVerification;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Students.AcceptInvite;

/// <summary>O que a página do convite mostra. Nome e e-mail só existem quando o convite é válido.</summary>
public sealed record InviteView(
    InviteState State,
    string? PersonalName = null,
    string? StudentName = null,
    string? Email = null,
    bool HasAccount = false,
    string? MaskedPhone = null,
    PhoneCodeStatus? Code = null)
{
    /// <summary>"Carlos", para "WhatsApp cadastrado pelo Carlos".</summary>
    public string PersonalFirstName => PhoneCodeDisplay.FirstName(PersonalName ?? string.Empty);
}

/// <summary>Resultado de pedir ou conferir o código do convite. <see cref="Proof"/> só quando confirmado.</summary>
public sealed record InviteCodeResult(
    InviteState State,
    PhoneCodeSendResult? Send = null,
    PhoneCodeVerifyResult? Verify = null,
    string? Proof = null);

public enum AcceptInviteOutcome
{
    Succeeded,
    Invalid,

    /// <summary>O convite deixou de ser válido (expirou, foi usado em paralelo etc.). Ver <see cref="AcceptInviteResult.State"/>.</summary>
    InviteUnavailable,

    /// <summary>Formulário de conta nova, mas o e-mail já tem conta: a página troca para o formulário de login.</summary>
    AccountExists,

    InvalidCredentials,
    LockedOut,

    /// <summary>A conta já está ligada a outro vínculo deste personal (por exemplo, um vínculo encerrado).</summary>
    AlreadyLinked,

    /// <summary>O formulário chegou sem a prova do código do WhatsApp (ou ela venceu): volta para o passo do código.</summary>
    PhoneNotVerified
}

public sealed record AcceptInviteResult(
    AcceptInviteOutcome Outcome,
    IReadOnlyDictionary<string, string> Errors,
    InviteState State = InviteState.Valid)
{
    public static AcceptInviteResult Of(AcceptInviteOutcome outcome, InviteState state = InviteState.Valid) =>
        new(outcome, new Dictionary<string, string>(), state);
}

/// <summary>
/// Aceite do convite (D-016, D-057). Antes do formulário, o aluno confirma com um código enviado por WhatsApp ao celular
/// que o personal cadastrou; o personal não vê o código. A prova do código é conferida de novo no envio do formulário.
/// Numa transação: marca o convite como usado com um UPDATE condicional (dois
/// aceites simultâneos não vencem os dois), cria ou reaproveita a conta, garante a role Student, ativa o
/// vínculo e audita. O login só acontece depois do commit. O token nunca vai para log nem auditoria.
/// </summary>
public sealed partial class AcceptInviteHandler(
    AppDbContext db,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    PhoneCodeService codes,
    InvitePhoneProof proofs,
    TimeProvider clock,
    ILogger<AcceptInviteHandler> logger)
{
    public const int NameMaxLength = 150;

    private sealed record InviteRow(
        Guid InviteId,
        Guid PersonalStudentId,
        Guid PersonalId,
        DateTime ExpiresAtUtc,
        DateTime? UsedAtUtc,
        DateTime? RevokedAtUtc,
        PersonalStudentStatus LinkStatus,
        bool PersonalActive,
        string StudentName,
        string StudentEmail,
        string StudentPhone,
        string PersonalName);

    public async Task<InviteView> GetAsync(string? token, CancellationToken cancellationToken)
    {
        var row = await FindAsync(token, cancellationToken);
        var state = StateOf(row);
        if (state != InviteState.Valid)
        {
            return new InviteView(state);
        }

        var hasAccount = await userManager.FindByEmailAsync(row!.StudentEmail) is not null;
        var code = await codes.GetStatusAsync(Scope(row), cancellationToken);
        return new InviteView(state, row.PersonalName, row.StudentName, row.StudentEmail, hasAccount,
            PhoneCodeRules.Mask(row.StudentPhone), code);
    }

    /// <summary>"Enviar código pelo WhatsApp": abrir o link não envia nada; só este POST envia.</summary>
    public async Task<InviteCodeResult> SendCodeAsync(string? token, string? correlationId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(token, cancellationToken);
        var state = StateOf(row);
        if (state != InviteState.Valid)
        {
            return new InviteCodeResult(state);
        }

        var sent = await codes.SendAsync(Scope(row!), recipientUserId: null, correlationId, cancellationToken);
        return new InviteCodeResult(state, Send: sent);
    }

    public async Task<InviteCodeResult> VerifyCodeAsync(
        string? token, string? code, string? correlationId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(token, cancellationToken);
        var state = StateOf(row);
        if (state != InviteState.Valid)
        {
            return new InviteCodeResult(state);
        }

        var verify = await codes.VerifyAsync(Scope(row!), code, correlationId, cancellationToken);
        var proof = verify.Outcome == PhoneCodeVerifyOutcome.Verified
            ? proofs.Create(row!.InviteId, row.StudentPhone, clock.GetUtcNow().UtcDateTime)
            : null;
        return new InviteCodeResult(state, Verify: verify, Proof: proof);
    }

    private static PhoneCodeScope Scope(InviteRow row) =>
        new(PhoneCodePurpose.InviteAcceptance, row.StudentPhone, PersonalStudentId: row.PersonalStudentId);

    private bool HasProof(InviteRow row, string? proof) =>
        proofs.IsValid(proof, row.InviteId, row.StudentPhone, clock.GetUtcNow().UtcDateTime);

    public async Task<AcceptInviteResult> AcceptWithNewAccountAsync(
        string? token, NewAccountInput input, string? correlationId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(token, cancellationToken);
        var state = StateOf(row);
        if (state != InviteState.Valid)
        {
            return AcceptInviteResult.Of(AcceptInviteOutcome.InviteUnavailable, state);
        }

        if (!HasProof(row!, input.PhoneProof))
        {
            return AcceptInviteResult.Of(AcceptInviteOutcome.PhoneNotVerified);
        }

        if (await userManager.FindByEmailAsync(row!.StudentEmail) is not null)
        {
            return AcceptInviteResult.Of(AcceptInviteOutcome.AccountExists);
        }

        var name = string.Join(' ', (input.Name ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = row.StudentEmail,
            Email = row.StudentEmail,
            FullName = name,
            // Quem abriu o link recebeu o convite pelo WhatsApp, não pelo e-mail: o e-mail não foi provado.
            EmailConfirmed = false,
            // O celular foi provado pelo código (D-057).
            PhoneNumber = row.StudentPhone,
            PhoneNumberConfirmed = true
        };

        var errors = await ValidateNewAccountAsync(user, name, input);
        if (errors.Count > 0)
        {
            return new AcceptInviteResult(AcceptInviteOutcome.Invalid, errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var claimed = await ClaimAsync(row, cancellationToken);
        if (claimed is not null)
        {
            return claimed;
        }

        var created = await userManager.CreateAsync(user, input.Password!);
        if (!created.Succeeded)
        {
            // Conta criada por outra requisição entre a checagem e agora.
            if (created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                    or nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                return AcceptInviteResult.Of(AcceptInviteOutcome.AccountExists);
            }

            return new AcceptInviteResult(AcceptInviteOutcome.Invalid, new Dictionary<string, string>
            {
                [nameof(NewAccountInput.Password)] = created.Errors.First().Description
            });
        }

        var linked = await LinkAsync(row, user, newAccount: true, correlationId, cancellationToken);
        if (linked is not null)
        {
            return linked;
        }

        await transaction.CommitAsync(cancellationToken);
        await signInManager.SignInAsync(user, isPersistent: false);
        LogAccepted(logger, row.PersonalStudentId, user.Id, true);
        return AcceptInviteResult.Of(AcceptInviteOutcome.Succeeded);
    }

    public async Task<AcceptInviteResult> AcceptWithExistingAccountAsync(
        string? token, ExistingAccountInput input, string? correlationId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(token, cancellationToken);
        var state = StateOf(row);
        if (state != InviteState.Valid)
        {
            return AcceptInviteResult.Of(AcceptInviteOutcome.InviteUnavailable, state);
        }

        // Antes da senha: sem a prova do código, não conta tentativa de senha nem aceita nada.
        if (!HasProof(row!, input.PhoneProof))
        {
            return AcceptInviteResult.Of(AcceptInviteOutcome.PhoneNotVerified);
        }

        var errors = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(input.Password))
        {
            errors[nameof(ExistingAccountInput.Password)] = "Informe sua senha.";
        }

        if (!input.AcceptTerms)
        {
            errors[nameof(ExistingAccountInput.AcceptTerms)] = TermsRequired;
        }

        if (errors.Count > 0)
        {
            return new AcceptInviteResult(AcceptInviteOutcome.Invalid, errors);
        }

        var user = await userManager.FindByEmailAsync(row!.StudentEmail);
        if (user is null)
        {
            // A conta foi removida entre a abertura da página e o envio: volta ao formulário de conta nova.
            return AcceptInviteResult.Of(AcceptInviteOutcome.InvalidCredentials);
        }

        // Mesma proteção do login: conta a falha e bloqueia após 5 tentativas.
        var check = await signInManager.CheckPasswordSignInAsync(user, input.Password!, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            LogLockedOut(logger, user.Id);
            return AcceptInviteResult.Of(AcceptInviteOutcome.LockedOut);
        }

        if (!check.Succeeded)
        {
            LogInvalidCredentials(logger, user.Id);
            return AcceptInviteResult.Of(AcceptInviteOutcome.InvalidCredentials);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var claimed = await ClaimAsync(row, cancellationToken);
        if (claimed is not null)
        {
            return claimed;
        }

        var linked = await LinkAsync(row, user, newAccount: false, correlationId, cancellationToken);
        if (linked is not null)
        {
            return linked;
        }

        await transaction.CommitAsync(cancellationToken);
        await signInManager.SignInAsync(user, isPersistent: false);
        LogAccepted(logger, row.PersonalStudentId, user.Id, false);
        return AcceptInviteResult.Of(AcceptInviteOutcome.Succeeded);
    }

    private const string TermsRequired = "Aceite os Termos de Uso e a Política de Privacidade para continuar.";

    private async Task<Dictionary<string, string>> ValidateNewAccountAsync(ApplicationUser user, string name, NewAccountInput input)
    {
        var errors = new Dictionary<string, string>();

        if (name.Length == 0)
        {
            errors[nameof(NewAccountInput.Name)] = "Informe seu nome.";
        }
        else if (name.Length > NameMaxLength)
        {
            errors[nameof(NewAccountInput.Name)] = $"Use no máximo {NameMaxLength} caracteres.";
        }

        if (string.IsNullOrEmpty(input.Password))
        {
            errors[nameof(NewAccountInput.Password)] = "Crie uma senha.";
        }
        else
        {
            // As mesmas regras do Identity que o CreateAsync aplicaria (8+, maiúscula, minúscula, número).
            foreach (var validator in userManager.PasswordValidators)
            {
                var result = await validator.ValidateAsync(userManager, user, input.Password);
                if (!result.Succeeded)
                {
                    errors[nameof(NewAccountInput.Password)] = result.Errors.First().Description;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(input.ConfirmPassword))
        {
            errors[nameof(NewAccountInput.ConfirmPassword)] = "Confirme a senha.";
        }
        else if (!string.Equals(input.Password, input.ConfirmPassword, StringComparison.Ordinal))
        {
            errors[nameof(NewAccountInput.ConfirmPassword)] = "As senhas não são iguais.";
        }

        if (!input.AcceptTerms)
        {
            errors[nameof(NewAccountInput.AcceptTerms)] = TermsRequired;
        }

        return errors;
    }

    /// <summary>
    /// Marca o convite como usado só se ele ainda estiver livre, dentro da transação. Uma segunda requisição
    /// simultânea espera o lock da linha e, após o commit da primeira, não encontra mais o convite livre.
    /// Retorna nulo quando esta requisição venceu.
    /// </summary>
    private async Task<AcceptInviteResult?> ClaimAsync(InviteRow row, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var updated = await db.StudentInvites
            .Where(i => i.Id == row.InviteId && i.UsedAtUtc == null && i.RevokedAtUtc == null && i.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.UsedAtUtc, now), cancellationToken);
        if (updated == 1)
        {
            return null;
        }

        var current = StateOf(await FindByIdAsync(row.InviteId, cancellationToken));
        LogClaimLost(logger, row.PersonalStudentId, current);
        return AcceptInviteResult.Of(AcceptInviteOutcome.InviteUnavailable,
            current == InviteState.Valid ? InviteState.Used : current);
    }

    private async Task<AcceptInviteResult?> LinkAsync(
        InviteRow row, ApplicationUser user, bool newAccount, string? correlationId, CancellationToken cancellationToken)
    {
        if (!await userManager.IsInRoleAsync(user, AppRoles.Student))
        {
            var role = await userManager.AddToRoleAsync(user, AppRoles.Student);
            if (!role.Succeeded)
            {
                throw new InvalidOperationException("Não foi possível atribuir a role Student.");
            }
        }

        // Conta existente: o celular confirmado passa a ser o deste convite (o salvamento abaixo grava junto).
        user.PhoneNumber = row.StudentPhone;
        user.PhoneNumberConfirmed = true;

        var now = clock.GetUtcNow().UtcDateTime;
        int updated;
        try
        {
            updated = await db.PersonalStudents
                .Where(s => s.Id == row.PersonalStudentId && s.Status == PersonalStudentStatus.Invited)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.StudentUserId, user.Id)
                    .SetProperty(x => x.Status, PersonalStudentStatus.Active)
                    .SetProperty(x => x.ActivatedAtUtc, now), cancellationToken);
        }
        catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState
                                   == PostgresErrorCodes.UniqueViolation)
        {
            // Índice (PersonalId, StudentUserId): a conta já tem outro vínculo com este personal.
            LogAlreadyLinked(logger, row.PersonalStudentId, user.Id);
            return AcceptInviteResult.Of(AcceptInviteOutcome.AlreadyLinked);
        }

        if (updated != 1)
        {
            return AcceptInviteResult.Of(AcceptInviteOutcome.InviteUnavailable, InviteState.NotFound);
        }

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = user.Id,
            Action = "InviteAccepted",
            EntityType = nameof(PersonalStudent),
            EntityId = row.PersonalStudentId,
            OccurredAtUtc = now,
            CorrelationId = correlationId,
            Metadata = JsonSerializer.Serialize(new { inviteId = row.InviteId, termsAccepted = true, newAccount, phoneVerified = true }),
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private InviteState StateOf(InviteRow? row) =>
        row is null
            ? InviteState.NotFound
            : InviteRules.Evaluate(row.ExpiresAtUtc, row.UsedAtUtc, row.RevokedAtUtc, row.LinkStatus,
                row.PersonalActive, clock.GetUtcNow().UtcDateTime);

    private Task<InviteRow?> FindAsync(string? token, CancellationToken cancellationToken)
    {
        // Busca pelo hash: o banco compara bytes do SHA-256, nunca o token em si.
        var hash = InviteToken.TryHash(token);
        return hash is null
            ? Task.FromResult<InviteRow?>(null)
            : Rows().Where(r => r.Invite.TokenHash == hash).Select(Project).SingleOrDefaultAsync(cancellationToken);
    }

    private Task<InviteRow?> FindByIdAsync(Guid inviteId, CancellationToken cancellationToken) =>
        Rows().Where(r => r.Invite.Id == inviteId).Select(Project).SingleOrDefaultAsync(cancellationToken);

    // Inicialização por propriedades (não construtor): o EF consegue traduzir filtros sobre o resultado.
    private sealed class JoinedRow
    {
        public required StudentInvite Invite { get; init; }
        public required PersonalStudent Link { get; init; }
        public required PersonalProfile Personal { get; init; }
    }

    private IQueryable<JoinedRow> Rows() =>
        from i in db.StudentInvites.AsNoTracking()
        join s in db.PersonalStudents on i.PersonalStudentId equals s.Id
        join p in db.PersonalProfiles on s.PersonalId equals p.Id
        select new JoinedRow { Invite = i, Link = s, Personal = p };

    private static readonly System.Linq.Expressions.Expression<Func<JoinedRow, InviteRow?>> Project = r => new InviteRow(
        r.Invite.Id, r.Link.Id, r.Personal.Id, r.Invite.ExpiresAtUtc, r.Invite.UsedAtUtc, r.Invite.RevokedAtUtc,
        r.Link.Status, r.Personal.IsActive, r.Link.StudentName, r.Link.StudentEmail, r.Link.StudentPhone,
        r.Personal.DisplayName);

    [LoggerMessage(EventId = 3100, Level = LogLevel.Information,
        Message = "Convite do vínculo {PersonalStudentId} aceito pelo usuário {UserId} (conta nova: {NewAccount})")]
    private static partial void LogAccepted(ILogger logger, Guid personalStudentId, Guid userId, bool newAccount);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning,
        Message = "Aceite de convite recusado: senha incorreta (usuário {UserId})")]
    private static partial void LogInvalidCredentials(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Warning,
        Message = "Aceite de convite recusado: conta bloqueada por excesso de tentativas (usuário {UserId})")]
    private static partial void LogLockedOut(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning,
        Message = "Aceite de convite do vínculo {PersonalStudentId} perdeu a disputa; situação atual {State}")]
    private static partial void LogClaimLost(ILogger logger, Guid personalStudentId, InviteState state);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Warning,
        Message = "Aceite de convite do vínculo {PersonalStudentId} recusado: usuário {UserId} já tem vínculo com o personal")]
    private static partial void LogAlreadyLinked(ILogger logger, Guid personalStudentId, Guid userId);
}
