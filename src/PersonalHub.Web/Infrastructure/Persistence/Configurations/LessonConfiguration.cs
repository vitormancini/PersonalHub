using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Domain.Lessons;
using PersonalHub.Web.Domain.Students;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        var statusCheck = builder.StoreAsString(x => x.Status, "Status", 16);

        builder.ToTable("Lesson", t =>
        {
            t.HasCheckConstraint("CK_Lesson_Status", statusCheck);
            t.HasCheckConstraint("CK_Lesson_UnitPrice", "\"UnitPrice\" > 0");
            t.HasCheckConstraint("CK_Lesson_DurationMinutes", "\"DurationMinutes\" IS NULL OR \"DurationMinutes\" > 0");
            t.HasCheckConstraint("CK_Lesson_Response",
                "(\"RespondedAtUtc\" IS NULL) = (\"RespondedByUserId\" IS NULL)");
            t.HasCheckConstraint("CK_Lesson_RejectionReason",
                "\"Status\" <> 'Rejected' OR \"RejectionReason\" IS NOT NULL");
            t.HasCheckConstraint("CK_Lesson_Deletion",
                "(\"DeletedAtUtc\" IS NULL) = (\"DeletedByUserId\" IS NULL)");
            t.HasCheckConstraint("CK_Lesson_DeletionReason",
                "\"DeletedAtUtc\" IS NULL OR \"Status\" = 'Pending' OR \"DeletionReason\" IS NOT NULL");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.UnitPrice).HasPrecision(12, 2);
        builder.Property(x => x.RejectionReason).HasMaxLength(500);
        builder.Property(x => x.DeletionReason).HasMaxLength(500);

        builder.HasIndex(x => new { x.PersonalStudentId, x.OccurredAtUtc }).HasFilter("\"DeletedAtUtc\" IS NULL");
        builder.HasIndex(x => new { x.PersonalStudentId, x.Status }).HasFilter("\"DeletedAtUtc\" IS NULL");

        builder.HasOne<PersonalStudent>().WithMany().HasForeignKey(x => x.PersonalStudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RespondedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DeletedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasXminConcurrencyToken();
    }
}
