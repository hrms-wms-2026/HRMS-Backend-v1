using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.RateLimiting;

/// <summary>
/// Append-only history. No foreign keys on purpose, so a history row outlives the plan, tenant or
/// admin it names.
/// </summary>
public sealed class RateLimitSettingChangeConfiguration : IEntityTypeConfiguration<RateLimitSettingChange>
{
    public const string TableName = "rate_limit_setting_changes";

    public void Configure(EntityTypeBuilder<RateLimitSettingChange> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_rate_limit_setting_changes_scope_ids",
                RateLimitSettingConfiguration.ScopeIdsCheck);
            table.HasCheckConstraint(
                "ck_rate_limit_setting_changes_limit_name",
                $"[limit_name] IN ({RateLimitSql.InList(RateLimitSettingRules.LimitNames)})");
            table.HasCheckConstraint(
                "ck_rate_limit_setting_changes_has_values",
                "[old_per_minute] IS NOT NULL OR [new_per_minute] IS NOT NULL");
            table.HasCheckConstraint(
                "ck_rate_limit_setting_changes_reason",
                "LTRIM([reason]) <> ''");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChangedAt).IsRequired();
        builder.Property(x => x.PlatformUserId).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(10).IsRequired();
        builder.Property(x => x.LimitName).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(100);

        builder.HasIndex(x => x.ChangedAt)
            .HasDatabaseName("ix_rate_limit_setting_changes_changed_at");
        builder.HasIndex(x => new { x.TenantId, x.ChangedAt })
            .HasDatabaseName("ix_rate_limit_setting_changes_tenant_changed_at");
    }
}
