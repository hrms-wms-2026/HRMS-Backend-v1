using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Screenshots;

public class MonitoringEvidenceAssetConfiguration : IEntityTypeConfiguration<MonitoringEvidenceAsset>
{
    public void Configure(EntityTypeBuilder<MonitoringEvidenceAsset> builder)
    {
        builder.ToTable("monitoring_evidence_assets");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EvidenceType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Source).HasMaxLength(50).IsRequired();
        builder.Property(e => e.TriggerType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.MetadataJson).HasColumnType("jsonb").HasDefaultValue("{}");

        // HR-side list by employee descending on capture time
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CapturedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_monitoring_evidence_assets_tenant_employee_captured");

        // Lookup by originating command
        builder.HasIndex(e => new { e.TenantId, e.AgentCommandId })
            .HasDatabaseName("ix_monitoring_evidence_assets_tenant_command");

        builder.HasOne<FileRecord>()
            .WithMany()
            .HasForeignKey(e => e.FileRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<TrayDeviceRegistration>()
            .WithMany()
            .HasForeignKey(e => e.AgentDeviceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<ActivitySnapshot>()
            .WithMany()
            .HasForeignKey(e => e.ActivitySnapshotId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<AgentCommand>()
            .WithMany()
            .HasForeignKey(e => e.AgentCommandId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
