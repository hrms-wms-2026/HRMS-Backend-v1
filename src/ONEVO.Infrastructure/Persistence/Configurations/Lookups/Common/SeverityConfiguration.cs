using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Lookups;

namespace ONEVO.Infrastructure.Persistence.Configurations.Lookups.Common;

public class SeverityConfiguration : IEntityTypeConfiguration<Severity>
{
    public void Configure(EntityTypeBuilder<Severity> builder)
    {
        builder.ToTable("severities");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Code).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Label).HasMaxLength(100).IsRequired();
        builder.HasIndex(e => e.Code).IsUnique();
    }
}
