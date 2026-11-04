using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Domain.Notifications;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class NotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutbox>
{
    public void Configure(EntityTypeBuilder<NotificationOutbox> builder)
    {
        var statusCheck = builder.StoreAsString(x => x.Status, "Status", 16);
        var channelCheck = builder.StoreAsString(x => x.Channel, "Channel", 16);

        builder.ToTable("NotificationOutbox", t =>
        {
            t.HasCheckConstraint("CK_NotificationOutbox_Status", statusCheck);
            t.HasCheckConstraint("CK_NotificationOutbox_Channel", channelCheck);
            t.HasCheckConstraint("CK_NotificationOutbox_Attempts", "\"Attempts\" >= 0");
            t.HasCheckConstraint("CK_NotificationOutbox_RecipientPhone_E164",
                $"\"RecipientPhone\" IS NULL OR \"RecipientPhone\" ~ '{ModelBuilderExtensions.E164Pattern}'");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RecipientPhone).HasMaxLength(20);
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(100);
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasXminConcurrencyToken();
    }
}
