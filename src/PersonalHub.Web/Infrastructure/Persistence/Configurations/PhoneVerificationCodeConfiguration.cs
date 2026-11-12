using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Domain.PhoneVerification;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class PhoneVerificationCodeConfiguration : IEntityTypeConfiguration<PhoneVerificationCode>
{
    public void Configure(EntityTypeBuilder<PhoneVerificationCode> builder)
    {
        var purposeCheck = builder.StoreAsString(x => x.Purpose, "Purpose", 24);

        builder.ToTable("PhoneVerificationCode", t =>
        {
            t.HasCheckConstraint("CK_PhoneVerificationCode_Purpose", purposeCheck);
            t.HasCheckConstraint("CK_PhoneVerificationCode_Phone_E164",
                $"\"PhoneE164\" ~ '{ModelBuilderExtensions.E164Pattern}'");
            t.HasCheckConstraint("CK_PhoneVerificationCode_Attempts", "\"Attempts\" BETWEEN 0 AND 5");
            // Convite: escopo é o vínculo (o aluno ainda não tem conta). Demais finalidades: a conta.
            t.HasCheckConstraint("CK_PhoneVerificationCode_Scope",
                "(\"Purpose\" = 'InviteAcceptance' AND \"PersonalStudentId\" IS NOT NULL AND \"UserId\" IS NULL) OR " +
                "(\"Purpose\" <> 'InviteAcceptance' AND \"UserId\" IS NOT NULL AND \"PersonalStudentId\" IS NULL)");
            t.HasCheckConstraint("CK_PhoneVerificationCode_Hash", "octet_length(\"CodeHash\") = 32");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.PhoneE164).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CodeHash).IsRequired();

        // Limites de envio e bloqueio contam por celular e finalidade.
        builder.HasIndex(x => new { x.PhoneE164, x.Purpose, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.PersonalStudentId, x.CreatedAtUtc }).HasFilter("\"PersonalStudentId\" IS NOT NULL");
        builder.HasIndex(x => new { x.UserId, x.Purpose, x.CreatedAtUtc }).HasFilter("\"UserId\" IS NOT NULL");

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PersonalStudent>().WithMany().HasForeignKey(x => x.PersonalStudentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasXminConcurrencyToken();
    }
}
