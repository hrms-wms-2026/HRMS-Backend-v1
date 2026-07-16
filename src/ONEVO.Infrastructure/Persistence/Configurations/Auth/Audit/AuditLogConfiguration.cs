using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Auth.Audit;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ResourceType).HasMaxLength(50);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.OldValuesJson).HasColumnType("jsonb");
        builder.Property(a => a.NewValuesJson).HasColumnType("jsonb");

        builder.HasIndex(a => new { a.TenantId, a.CreatedAt });
        builder.HasIndex(a => new { a.UserId, a.CreatedAt });
    }
}
