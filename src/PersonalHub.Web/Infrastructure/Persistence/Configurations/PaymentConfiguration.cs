using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Domain.Billing;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        var statusCheck = builder.StoreAsString(x => x.Status, "Status", 16);

        builder.ToTable("Payment", t =>
        {
            t.HasCheckConstraint("CK_Payment_Status", statusCheck);
            t.HasCheckConstraint("CK_Payment_Amount", "\"Amount\" > 0");
            t.HasCheckConstraint("CK_Payment_Decision",
                "(\"DecidedAtUtc\" IS NULL) = (\"DecidedByUserId\" IS NULL) AND (\"Status\" = 'Reported') = (\"DecidedAtUtc\" IS NULL)");
            // A mensagem do personal só existe na recusa (D-051).
            t.HasCheckConstraint("CK_Payment_DecisionNote", "\"DecisionNote\" IS NULL OR \"Status\" = 'Rejected'");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(12, 2);
        builder.Property(x => x.Notes).HasMaxLength(Payment.TextMaxLength);
        builder.Property(x => x.DecisionNote).HasMaxLength(Payment.TextMaxLength);

        // No máximo um pagamento ativo (informado ou confirmado) por período (D-024).
        builder.HasIndex(x => x.BillingPeriodId)
            .IsUnique()
            .HasFilter("\"Status\" IN ('Reported', 'Confirmed')")
            .HasDatabaseName("IX_Payment_BillingPeriodId_Active");

        builder.HasOne<BillingPeriod>().WithMany().HasForeignKey(x => x.BillingPeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReportedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Histórico do demonstrativo (D-053).
        builder.HasIndex(x => new { x.BillingPeriodId, x.ReportedAtUtc }).HasDatabaseName("IX_Payment_BillingPeriodId_ReportedAtUtc");

        builder.HasXminConcurrencyToken();
    }
}
