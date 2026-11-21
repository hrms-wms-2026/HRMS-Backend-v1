using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.Monitoring.Settings.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.Monitoring.Settings;

public sealed class MonitoringSettingConfiguration : IEntityTypeConfiguration<MonitoringSetting>
{
    public const string TableName = "monitoring_settings";

    /// <summary>The on/off switch columns; a base row must set every one of them.</summary>
    internal static readonly string[] SwitchColumns =
    [
        "activity_monitoring", "application_tracking", "document_tracking", "communication_tracking",
        "screenshot_capture", "auto_screenshot_capture", "meeting_detection", "device_tracking",
        "work_location_verification", "identity_verification", "biometric",
    ];

    public void Configure(EntityTypeBuilder<MonitoringSetting> builder)
    {
        static string InList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_monitoring_settings_scope_type",
                $"[scope_type] IN ({InList(MonitoringSettingScopes.All)})");
            table.HasCheckConstraint(
                "ck_monitoring_settings_base_rows_set_every_switch",
                $"[scope_type] NOT IN ({InList(MonitoringSettingScopes.Base)}) OR ("
                + string.Join(" AND ", SwitchColumns.Select(c => $"[{c}] IS NOT NULL")) + ")");
            table.HasCheckConstraint(
                "ck_monitoring_settings_tenant_scope_id",
                $"[scope_type] <> '{MonitoringSettingScopes.Tenant}' OR [scope_id] = [tenant_id]");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.ScopeType).HasMaxLength(20).IsRequired();
        builder.Property(e => e.OverrideReason).HasMaxLength(500);
        builder.Ignore(e => e.IsBase);

        builder.HasIndex(e => new { e.TenantId, e.ScopeType, e.ScopeId })
            .IsUnique()
            .HasDatabaseName("ux_monitoring_settings_tenant_scope");
        // The resolver reads every level of one employee in one query: tenant_id = @t AND scope_id IN (...).
        builder.HasIndex(e => new { e.TenantId, e.ScopeId })
            .HasDatabaseName("ix_monitoring_settings_tenant_scope_id");

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
