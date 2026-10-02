using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.WorkManagement.Monitoring.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.WorkManagement;

public class MonitorAlertConfiguration : IEntityTypeConfiguration<MonitorAlert>
{
    public void Configure(EntityTypeBuilder<MonitorAlert> builder)
    {
        builder.ToTable("wm_monitor_alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.TargetType).HasMaxLength(20).IsRequired();
        builder.Property(a => a.TargetTitle).HasMaxLength(500).IsRequired();
        builder.Property(a => a.RuleCode).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Message).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.DetailsJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.ProjectId, a.ResolvedAt })
            .HasDatabaseName("ix_wm_monitor_alerts_tenant_id_project_id_resolved_at");

        // At most one OPEN alert per problem; resolved rows are kept as history.
        builder.HasIndex(a => new { a.TenantId, a.TargetId, a.RuleCode, a.SubjectEmployeeId })
            .HasDatabaseName("ux_wm_monitor_alerts_open_key")
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("resolved_at IS NULL");
    }
}
