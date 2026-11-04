using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.Audit.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Audit;

public class PlatformAuditLogConfiguration : IEntityTypeConfiguration<PlatformAuditLog>
{
    public void Configure(EntityTypeBuilder<PlatformAuditLog> builder)
    {
        builder.ToTable("platform_audit_logs");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Action).HasMaxLength(100).IsRequired();
        builder.Property(e => e.TargetType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.TargetId).HasMaxLength(200);
        builder.Property(e => e.BeforeJson).HasColumnType("jsonb");
        builder.Property(e => e.AfterJson).HasColumnType("jsonb");
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasMaxLength(512);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);
        builder.Property(e => e.CreatedAt).IsRequired();

        // No foreign keys on purpose: an audit row must outlive the actor, the target and the
        // tenant it refers to, and a cascading delete would be an UPDATE/DELETE on this table.
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => new { e.ActorPlatformUserId, e.CreatedAt });
        builder.HasIndex(e => new { e.TargetType, e.TargetId });
        builder.HasIndex(e => new { e.TargetTenantId, e.CreatedAt });
    }
}
