using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PersonalHub.Web.Domain.Students;

namespace PersonalHub.Web.Infrastructure.Persistence.Configurations;

public class StudentInviteConfiguration : IEntityTypeConfiguration<StudentInvite>
{
    public void Configure(EntityTypeBuilder<StudentInvite> builder)
    {
        builder.ToTable("StudentInvite", t =>
        {
            // SHA-256 sempre tem 32 bytes.
            t.HasCheckConstraint("CK_StudentInvite_TokenHash_Length", "octet_length(\"TokenHash\") = 32");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).IsRequired();

        builder.HasIndex(x => x.TokenHash).IsUnique();

        builder.HasOne<PersonalStudent>().WithMany().HasForeignKey(x => x.PersonalStudentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasXminConcurrencyToken();
    }
}
