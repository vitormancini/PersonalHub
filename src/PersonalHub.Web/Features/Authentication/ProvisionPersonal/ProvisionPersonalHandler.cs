using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Identity;
using PersonalHub.Web.Infrastructure.Persistence;

namespace PersonalHub.Web.Features.Authentication.ProvisionPersonal;

public sealed record ProvisionPersonalRequest(string Email, string Name, string? Phone, string Password);

public sealed record ProvisionPersonalResult(Guid? UserId, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public static ProvisionPersonalResult Failed(params IEnumerable<string> errors) => new(null, errors.Distinct().ToList());
}

/// <summary>
/// Cria a conta de um personal (D-027): usuário, role Personal e PersonalProfile ativo, numa única transação.
/// Se qualquer passo falhar, nada fica gravado.
/// </summary>
public sealed partial class ProvisionPersonalHandler(UserManager<ApplicationUser> userManager, AppDbContext db)
{
    public const int NameMaxLength = 120;
    public const int EmailMaxLength = 256;

    public async Task<ProvisionPersonalResult> HandleAsync(ProvisionPersonalRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var name = request.Name.Trim();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

        var errors = Validate(email, name, phone);
        if (errors.Count > 0)
        {
            return ProvisionPersonalResult.Failed(errors);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };

        // O UserManager valida a senha pelas regras do Identity antes de gravar qualquer coisa.
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return ProvisionPersonalResult.Failed(created.Errors.Select(e => e.Description));
        }

        var role = await userManager.AddToRoleAsync(user, AppRoles.Personal);
        if (!role.Succeeded)
        {
            return ProvisionPersonalResult.Failed(role.Errors.Select(e => e.Description));
        }

        var now = DateTime.UtcNow;
        var profile = new PersonalProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DisplayName = name,
            PhoneNumber = phone,
            IsActive = true,
            CreatedAtUtc = now
        };
        db.PersonalProfiles.Add(profile);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = null,
            Action = "PersonalProvisioned",
            EntityType = nameof(PersonalProfile),
            EntityId = profile.Id,
            OccurredAtUtc = now,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new ProvisionPersonalResult(user.Id, []);
    }

    private static List<string> Validate(string email, string name, string? phone)
    {
        var errors = new List<string>();

        if (email.Length == 0 || email.Length > EmailMaxLength || !new EmailAddressAttribute().IsValid(email))
        {
            errors.Add("Informe um e-mail válido (--email).");
        }

        if (name.Length == 0 || name.Length > NameMaxLength)
        {
            errors.Add($"Informe o nome com até {NameMaxLength} caracteres (--name).");
        }

        if (phone is not null && !E164().IsMatch(phone))
        {
            errors.Add("Celular deve estar no formato internacional, por exemplo +5511999990000 (--phone).");
        }

        return errors;
    }

    [GeneratedRegex(ModelBuilderExtensions.E164Pattern)]
    private static partial Regex E164();
}
