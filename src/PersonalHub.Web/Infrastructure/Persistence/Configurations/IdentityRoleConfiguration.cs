using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class IdentityRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        // ConcurrencyStamp fixo para o seed não gerar alterações a cada nova migration.
        builder.HasData(
            new IdentityRole<Guid>
            {
                Id = AppRoles.PersonalId,
                Name = AppRoles.Personal,
                NormalizedName = AppRoles.Personal.ToUpperInvariant(),
                ConcurrencyStamp = "b1e0d4c3-0001-4c5e-8a77-5e0f6a2d9001"
            },
            new IdentityRole<Guid>
            {
                Id = AppRoles.StudentId,
                Name = AppRoles.Student,
                NormalizedName = AppRoles.Student.ToUpperInvariant(),
                ConcurrencyStamp = "b1e0d4c3-0002-4c5e-8a77-5e0f6a2d9002"
            });
    }
}
