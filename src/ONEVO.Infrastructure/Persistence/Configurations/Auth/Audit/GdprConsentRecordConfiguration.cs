using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Audit;

public class GdprConsentRecordConfiguration : IEntityTypeConfiguration<GdprConsentRecord>
{
    public void Configure(EntityTypeBuilder<GdprConsentRecord> builder)
    {
        builder.ToTable("gdpr_consent_records");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.ConsentType).HasMaxLength(50).IsRequired();
        builder.Property(g => g.IpAddress).HasMaxLength(45);

        builder.HasIndex(g => new { g.TenantId, g.UserId, g.ConsentType });
    }
}
