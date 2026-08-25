using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public class FeatureFlagSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<FeatureFlagSeeder> _logger;

    public FeatureFlagSeeder(IServiceProvider services, ILogger<FeatureFlagSeeder> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await SeedAsync(db, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Feature flag seeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken ct)
    {
        // Phase 1 Runtime Flag Seed List — 2nd brain/OneVo-HR/developer-platform/modules/feature-flag-manager/end-to-end-logic.md, lines 109-144
        var flagsToSeed = new[]
        {
            // Core flags
            new { Key = "time_off.accrual_rules", Module = "time_off", Feature = (string?)"time_off.accrual_rules", Default = true, Rollout = 100, Description = "Staged rollout of advanced accrual logic" },
            new { Key = "monitoring.website_usage", Module = "monitoring", Feature = (string?)"monitoring.website_usage", Default = false, Rollout = 0, Description = "Privacy-sensitive tracking" },
            new { Key = "monitoring.screenshot_on_demand", Module = "monitoring", Feature = (string?)"monitoring.screenshot_on_demand", Default = false, Rollout = 0, Description = "Privacy-sensitive screenshot" },
            new { Key = "monitoring.app_allowlist", Module = "monitoring", Feature = (string?)"monitoring.app_allowlist", Default = true, Rollout = 100, Description = "Emergency disable of allowlist" },
            new { Key = "monitoring.productivity_classification", Module = "monitoring", Feature = (string?)"monitoring.productivity_classification", Default = true, Rollout = 100, Description = "Rollback classification rules" },
            new { Key = "monitoring.attendance_corrections", Module = "monitoring", Feature = (string?)"monitoring.attendance_corrections", Default = true, Rollout = 100, Description = "Attendance correction rollout" },
            new { Key = "monitoring.biometric_devices", Module = "monitoring", Feature = (string?)"monitoring.biometric_devices", Default = false, Rollout = 0, Description = "Hardware-dependent rollout" },
            new { Key = "verification.face_match", Module = "verification", Feature = (string?)"verification.face_match", Default = false, Rollout = 0, Description = "AI/biometric rollout" },
            new { Key = "verification.manual_review", Module = "verification", Feature = (string?)"verification.manual_review", Default = true, Rollout = 100, Description = "Disable manual review queue" },
            new { Key = "verification.photo_challenge", Module = "verification", Feature = (string?)"verification.photo_challenge", Default = false, Rollout = 0, Description = "Camera/biometric challenge" },
            new { Key = "analytics.productivity_dashboard", Module = "analytics", Feature = (string?)"analytics.productivity_dashboard", Default = true, Rollout = 100, Description = "Dashboard rollout control" },
            new { Key = "analytics.data_export", Module = "analytics", Feature = (string?)"analytics.data_export", Default = false, Rollout = 0, Description = "Sensitive data export access" },
            new { Key = "analytics.scheduled_reports", Module = "analytics", Feature = (string?)"analytics.scheduled_reports", Default = false, Rollout = 0, Description = "Background report rollout" },
            new { Key = "work_management.resource_planning", Module = "work_management", Feature = (string?)"work_management.resource_planning", Default = false, Rollout = 0, Description = "Advanced WorkSync feature" },
            new { Key = "work_management.work_analytics", Module = "work_management", Feature = (string?)"work_management.work_analytics", Default = false, Rollout = 0, Description = "WorkSync analytics rollout" },
            new { Key = "work_management.github_integration", Module = "work_management", Feature = (string?)"work_management.github_integration", Default = false, Rollout = 0, Description = "Integration-dependent" },
            new { Key = "integrations.microsoft_teams", Module = "integrations", Feature = (string?)"integrations.microsoft_teams", Default = false, Rollout = 0, Description = "Tenant integration rollout" },
            new { Key = "integrations.github", Module = "integrations", Feature = (string?)"integrations.github", Default = false, Rollout = 0, Description = "Tenant integration rollout" },
            new { Key = "integrations.webhooks", Module = "integrations", Feature = (string?)"integrations.webhooks", Default = false, Rollout = 0, Description = "Webhook surface rollout" },
            new { Key = "integrations.api_access", Module = "integrations", Feature = (string?)"integrations.api_access", Default = false, Rollout = 0, Description = "Public/API access rollout" },

            // Optional operational safeguard flags — must not be sold as plan features
            new { Key = "auth.optional_google_oauth", Module = "auth", Feature = (string?)"auth.optional_google_oauth", Default = true, Rollout = 100, Description = "Operational safeguard — not a sellable plan feature" },
            new { Key = "auth.mfa_challenge_enforcement", Module = "auth", Feature = (string?)null, Default = true, Rollout = 100, Description = "Operational safeguard — not a sellable plan feature" },
            new { Key = "notifications.email_delivery", Module = "notifications", Feature = (string?)"notifications.email_delivery", Default = true, Rollout = 100, Description = "Operational safeguard — not a sellable plan feature" },
            new { Key = "notifications.in_app_delivery", Module = "notifications", Feature = (string?)"notifications.in_app_delivery", Default = true, Rollout = 100, Description = "Operational safeguard — not a sellable plan feature" },
        };

        var existingModuleKeys = new HashSet<string>(await db.ModuleCatalog.Select(m => m.ModuleKey).ToListAsync(ct), StringComparer.Ordinal);
        var existingFeatureKeys = new HashSet<string>(await db.ModuleFeatures.Select(f => f.FeatureKey).ToListAsync(ct), StringComparer.Ordinal);

        var existingFlags = await db.FeatureFlags.ToListAsync(ct);
        var existingByKey = existingFlags.ToDictionary(f => f.Key, StringComparer.Ordinal);

        foreach (var def in flagsToSeed)
        {
            var moduleKey = existingModuleKeys.Contains(def.Module) ? def.Module : null;
            var featureKey = def.Feature is not null && existingFeatureKeys.Contains(def.Feature) ? def.Feature : null;

            if (existingByKey.TryGetValue(def.Key, out var flag))
            {
                flag.Description = def.Description;
                flag.DefaultValue = def.Default;
                flag.RolloutPercentage = def.Rollout;
                flag.ModuleKey = moduleKey;
                flag.FeatureKey = featureKey;
                flag.IsActive = true;
                flag.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                db.FeatureFlags.Add(new FeatureFlag
                {
                    Key = def.Key,
                    Description = def.Description,
                    DefaultValue = def.Default,
                    RolloutPercentage = def.Rollout,
                    ModuleKey = moduleKey,
                    FeatureKey = featureKey,
                    IsActive = true
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
