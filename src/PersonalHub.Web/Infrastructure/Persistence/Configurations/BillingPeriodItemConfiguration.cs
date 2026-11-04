using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Lessons;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class BillingPeriodItemConfiguration : IEntityTypeConfiguration<BillingPeriodItem>
{
    public void Configure(EntityTypeBuilder<BillingPeriodItem> builder)
    {
        var kindCheck = builder.StoreAsString(x => x.Kind, "Kind", 16);

        builder.ToTable("BillingPeriodItem", t =>
        {
            t.HasCheckConstraint("CK_BillingPeriodItem_Kind", kindCheck);
            t.HasCheckConstraint("CK_BillingPeriodItem_Amount",
                "(\"Kind\" = 'Lesson' AND \"Amount\" > 0) OR (\"Kind\" = 'Adjustment' AND \"Amount\" < 0)");
            t.HasCheckConstraint("CK_BillingPeriodItem_LessonId",
                "\"Kind\" <> 'Lesson' OR \"LessonId\" IS NOT NULL");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Description).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(12, 2);

        // Uma aula é cobrada no máximo uma vez e creditada no máximo uma vez (D-020, D-023).
        builder.HasIndex(x => new { x.LessonId, x.Kind }).IsUnique();

        builder.HasOne<BillingPeriod>().WithMany().HasForeignKey(x => x.BillingPeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);

        builder.HasXminConcurrencyToken();
    }
}
