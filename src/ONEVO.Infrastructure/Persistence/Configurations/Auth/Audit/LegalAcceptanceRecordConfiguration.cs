using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Audit;

public class LegalAcceptanceRecordConfiguration : IEntityTypeConfiguration<LegalAcceptanceRecord>
{
    public void Configure(EntityTypeBuilder<LegalAcceptanceRecord> builder)
    {
        builder.ToTable("legal_acceptance_records");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.DocumentType).HasMaxLength(80).IsRequired();
        builder.Property(g => g.DocumentVersion).HasMaxLength(50).IsRequired();
        builder.Property(g => g.Decision).HasMaxLength(20).IsRequired();
        builder.Property(g => g.Required).IsRequired();
        builder.Property(g => g.DecidedAt).IsRequired();
        builder.Property(g => g.IpAddress).HasMaxLength(45);
        builder.Property(g => g.UserAgent).HasMaxLength(500);
        builder.Property(g => g.Source).HasMaxLength(30).IsRequired();

        builder.HasIndex(g => new { g.TenantId, g.UserId, g.DocumentType });
    }
}
