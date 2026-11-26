using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.Audit.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;

namespace ONEVO.Infrastructure.Persistence.Configurations.DevPlatform.Audit;

public class PlatformAuditLogConfiguration : IEntityTypeConfiguration<PlatformAuditLog>
{
    public void Configure(EntityTypeBuilder<PlatformAuditLog> builder)
    {
        // Platform-wide and append-only. No foreign keys on purpose: an audit row must outlive the
        // platform user, tenant or record it names, and must never block their deletion.
        builder.ToTable("platform_audit_logs", t => t.HasCheckConstraint(
            "ck_platform_audit_logs_outcome",
            $"outcome IN ('{PlatformAuditLog.OutcomeSucceeded}', '{PlatformAuditLog.OutcomeFailed}')"));
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Action).HasMaxLength(100).IsRequired();
        builder.Property(e => e.TargetType).HasMaxLength(80).IsRequired();
        builder.Property(e => e.TargetId).HasMaxLength(200);
        builder.Property(e => e.Outcome).HasMaxLength(20).IsRequired();
        builder.Property(e => e.FailureReason).HasMaxLength(200);
        builder.Property(e => e.BeforeJson).IsJsonColumn();
        builder.Property(e => e.AfterJson).IsJsonColumn();
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.UserAgent).HasMaxLength(512);
        builder.Property(e => e.CorrelationId).HasMaxLength(100);

        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => new { e.ActorPlatformUserId, e.CreatedAt });
        builder.HasIndex(e => new { e.TargetType, e.TargetId });
        builder.HasIndex(e => new { e.TargetTenantId, e.CreatedAt });
        builder.HasIndex(e => new { e.Action, e.CreatedAt });
    }
}
