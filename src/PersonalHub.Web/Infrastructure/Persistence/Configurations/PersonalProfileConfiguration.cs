using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class PersonalProfileConfiguration : IEntityTypeConfiguration<PersonalProfile>
{
    public void Configure(EntityTypeBuilder<PersonalProfile> builder)
    {
        builder.ToTable("PersonalProfile", t =>
        {
            t.HasCheckConstraint("CK_PersonalProfile_PhoneNumber_E164",
                $"\"PhoneNumber\" IS NULL OR \"PhoneNumber\" ~ '{ModelBuilderExtensions.E164Pattern}'");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.PhoneNumber).HasMaxLength(20);

        builder.HasIndex(x => x.UserId).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasXminConcurrencyToken();
    }
}
