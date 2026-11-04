using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Domain.Students;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class BillingPeriodConfiguration : IEntityTypeConfiguration<BillingPeriod>
{
    public void Configure(EntityTypeBuilder<BillingPeriod> builder)
    {
        var statusCheck = builder.StoreAsString(x => x.Status, "Status", 20);

        builder.ToTable("BillingPeriod", t =>
        {
            t.HasCheckConstraint("CK_BillingPeriod_Status", statusCheck);
            t.HasCheckConstraint("CK_BillingPeriod_ReferenceMonth", "EXTRACT(DAY FROM \"ReferenceMonth\") = 1");
            t.HasCheckConstraint("CK_BillingPeriod_Range", "\"PeriodEnd\" > \"PeriodStart\"");
            t.HasCheckConstraint("CK_BillingPeriod_TotalAmount", "\"TotalAmount\" >= 0");
            t.HasCheckConstraint("CK_BillingPeriod_PaidAtUtc", "(\"Status\" = 'Paid') = (\"PaidAtUtc\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TotalAmount).HasPrecision(12, 2);

        // Torna o fechamento idempotente: um demonstrativo por vínculo e mês (D-019).
        builder.HasIndex(x => new { x.PersonalStudentId, x.ReferenceMonth }).IsUnique();

        builder.HasOne<PersonalStudent>().WithMany().HasForeignKey(x => x.PersonalStudentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasXminConcurrencyToken();
    }
}
