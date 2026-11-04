using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Domain.Auditing;
using PersonalHub.Web.Infrastructure.Identity;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLog");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.Metadata).HasColumnType("jsonb");

        builder.HasIndex(x => new { x.EntityType, x.EntityId });

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
