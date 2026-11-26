using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting.Entities;

namespace ONEVO.Infrastructure.Persistence.Configurations.SharedPlatform.RateLimiting;

public sealed class RateLimitSettingConfiguration : IEntityTypeConfiguration<RateLimitSetting>
{
    public const string TableName = "rate_limit_settings";

    public void Configure(EntityTypeBuilder<RateLimitSetting> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_rate_limit_settings_scope_ids", ScopeIdsCheck);
            table.HasCheckConstraint(
                "ck_rate_limit_settings_limit_name",
                $"[limit_name] IN ({RateLimitSql.InList(RateLimitSettingRules.LimitNames)})");
            table.HasCheckConstraint(
                "ck_rate_limit_settings_platform_limits_global_only",
                $"[scope] = '{RateLimitSettingRules.GlobalScope}' " +
                $"OR [limit_name] IN ({RateLimitSql.InList(RateLimitSettingRules.PerTenantLimitNames)})");
            table.HasCheckConstraint(
                "ck_rate_limit_settings_burst",
                $"([limit_name] IN ({RateLimitSql.InList(RateLimitSettingRules.SlidingWindowLimitNames)}) AND [burst] IS NULL) " +
                $"OR ([limit_name] NOT IN ({RateLimitSql.InList(RateLimitSettingRules.SlidingWindowLimitNames)}) AND [burst] IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_rate_limit_settings_ranges",
                $"[per_minute] BETWEEN {RateLimitSettingRules.MinPerMinute} AND {RateLimitSettingRules.MaxPerMinute} " +
                $"AND ([burst] IS NULL OR [burst] BETWEEN {RateLimitSettingRules.MinBurst} AND {RateLimitSettingRules.MaxBurst})");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Scope).HasMaxLength(10).IsRequired();
        builder.Property(x => x.LimitName).HasMaxLength(40).IsRequired();
        builder.Property(x => x.PerMinute).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.UpdatedByPlatformUserId).IsRequired();
        builder.Property<ulong>("RowVersion").IsRowVersion();

        // At most one row per scope and limit.
        builder.HasIndex(x => x.LimitName)
            .IsUnique()
            .HasDatabaseName("ux_rate_limit_settings_global")
            .HasFilter($"[scope] = '{RateLimitSettingRules.GlobalScope}'");
        builder.HasIndex(x => new { x.SubscriptionPlanId, x.LimitName })
            .IsUnique()
            .HasDatabaseName("ux_rate_limit_settings_plan")
            .HasFilter($"[scope] = '{RateLimitSettingRules.PlanScope}' AND [subscription_plan_id] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.LimitName })
            .IsUnique()
            .HasDatabaseName("ux_rate_limit_settings_tenant")
            .HasFilter($"[scope] = '{RateLimitSettingRules.TenantScope}' AND [tenant_id] IS NOT NULL");

        builder.HasOne<SubscriptionPlan>()
            .WithMany()
            .HasForeignKey(x => x.SubscriptionPlanId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformUser>()
            .WithMany()
            .HasForeignKey(x => x.UpdatedByPlatformUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    internal static readonly string ScopeIdsCheck =
        $"([scope] = '{RateLimitSettingRules.GlobalScope}' AND [subscription_plan_id] IS NULL AND [tenant_id] IS NULL) " +
        $"OR ([scope] = '{RateLimitSettingRules.PlanScope}' AND [subscription_plan_id] IS NOT NULL AND [tenant_id] IS NULL) " +
        $"OR ([scope] = '{RateLimitSettingRules.TenantScope}' AND [tenant_id] IS NOT NULL AND [subscription_plan_id] IS NULL)";
}

internal static class RateLimitSql
{
    public static string InList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));
}
