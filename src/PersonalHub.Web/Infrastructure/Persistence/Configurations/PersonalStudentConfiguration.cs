using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class PersonalStudentConfiguration : IEntityTypeConfiguration<PersonalStudent>
{
    public void Configure(EntityTypeBuilder<PersonalStudent> builder)
    {
        var statusCheck = builder.StoreAsString(x => x.Status, "Status", 16);

        builder.ToTable("PersonalStudent", t =>
        {
            t.HasCheckConstraint("CK_PersonalStudent_Status", statusCheck);
            t.HasCheckConstraint("CK_PersonalStudent_LessonPrice", "\"LessonPrice\" > 0");
            t.HasCheckConstraint("CK_PersonalStudent_BillingDueDay", "\"BillingDueDay\" BETWEEN 1 AND 31");
            t.HasCheckConstraint("CK_PersonalStudent_EndedAtUtc",
                "\"Status\" <> 'Inactive' OR \"EndedAtUtc\" IS NOT NULL");
            t.HasCheckConstraint("CK_PersonalStudent_StudentEmail_Normalized",
                "\"StudentEmail\" = lower(btrim(\"StudentEmail\"))");
            t.HasCheckConstraint("CK_PersonalStudent_StudentPhone_E164",
                $"\"StudentPhone\" ~ '{ModelBuilderExtensions.E164Pattern}'");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.StudentName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.StudentEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.StudentPhone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LessonPrice).HasPrecision(12, 2);

        builder.HasIndex(x => new { x.PersonalId, x.StudentUserId })
            .IsUnique()
            .HasFilter("\"StudentUserId\" IS NOT NULL");
        builder.HasIndex(x => new { x.PersonalId, x.StudentEmail })
            .IsUnique()
            .HasFilter("\"Status\" <> 'Inactive'");
        builder.HasIndex(x => new { x.StudentUserId, x.Status });
        builder.HasIndex(x => new { x.PersonalId, x.Status });

        builder.HasOne<PersonalProfile>().WithMany().HasForeignKey(x => x.PersonalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasXminConcurrencyToken();
    }
}
