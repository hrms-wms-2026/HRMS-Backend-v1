using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public class ModuleCatalogSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ModuleCatalogSeeder> _logger;

    public ModuleCatalogSeeder(IServiceProvider services, ILogger<ModuleCatalogSeeder> logger)
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
            _logger.LogError(ex, "Module catalog seeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(ApplicationDbContext db, CancellationToken ct)
    {
        await SeedModulesAsync(db, ct);
        await SeedFeaturesAsync(db, ct);
        await SeedPermissionOwnershipAsync(db, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedModulesAsync(ApplicationDbContext db, CancellationToken ct)
    {
        // Canonical Phase 1 Modules based on Phase 1 exclusions
        var modulesToSeed = new[]
        {
            // Foundation
            new { Key = "auth", Name = "Auth & Security", Pillar = "shared", Phase = "phase_1", Unit = "flat_rate" },
            new { Key = "configuration", Name = "Configuration", Pillar = "shared", Phase = "phase_1", Unit = "flat_rate" },
            new { Key = "roles", Name = "Roles & Permissions", Pillar = "shared", Phase = "phase_1", Unit = "flat_rate" },
            new { Key = "notifications", Name = "Notifications", Pillar = "shared", Phase = "phase_1", Unit = "flat_rate" },
            new { Key = "org", Name = "Org Structure", Pillar = "shared", Phase = "phase_1", Unit = "flat_rate" },
            
            // HR Core
            new { Key = "core_hr", Name = "Core HR", Pillar = "hr_management", Phase = "phase_1", Unit = "per_employee" },
            new { Key = "time_off", Name = "Time Off", Pillar = "hr_management", Phase = "phase_1", Unit = "per_employee" },
            new { Key = "calendar", Name = "Calendar", Pillar = "hr_management", Phase = "phase_1", Unit = "per_employee" },
            new { Key = "time_attendance", Name = "Time & Attendance", Pillar = "hr_management", Phase = "phase_1", Unit = "per_employee" },

            // Intelligence
            new { Key = "monitoring", Name = "Activity Monitoring", Pillar = "workforce_intelligence", Phase = "phase_1", Unit = "per_employee" },
            new { Key = "verification", Name = "Identity Verification", Pillar = "workforce_intelligence", Phase = "phase_1", Unit = "per_employee" },
            new { Key = "analytics", Name = "Analytics & Reports", Pillar = "workforce_intelligence", Phase = "phase_1", Unit = "per_employee" },

            // WorkSync
            new { Key = "work_management", Name = "Work Management", Pillar = "worksync", Phase = "phase_1", Unit = "per_employee" },
            new { Key = "integrations", Name = "Integrations", Pillar = "worksync", Phase = "phase_1", Unit = "flat_rate" }
        };

        var existingModules = await db.ModuleCatalog.ToListAsync(ct);
        var existingByKey = existingModules.ToDictionary(m => m.ModuleKey, StringComparer.Ordinal);

        foreach (var def in modulesToSeed)
        {
            if (existingByKey.TryGetValue(def.Key, out var module))
            {
                module.Name = def.Name;
                module.Pillar = def.Pillar;
                module.Phase = def.Phase;
                module.PricingUnit = def.Unit;
            }
            else
            {
                db.ModuleCatalog.Add(new ModuleCatalogItem
                {
                    ModuleKey = def.Key,
                    Name = def.Name,
                    Pillar = def.Pillar,
                    Phase = def.Phase,
                    PricingUnit = def.Unit,
                    PricingReference = "[]",
                    StorageReference = "[]",
                    AiTokenReference = "[]",
                    IsActive = true
                });
            }
        }
    }

    private static async Task SeedFeaturesAsync(ApplicationDbContext db, CancellationToken ct)
    {
        // Map features exactly based on phase-1-feature-permission-map.md
        var featuresToSeed = new[]
        {
            // Foundation
            new { Key = "auth.optional_google_oauth", Module = "auth", Name = "Optional Google OAuth", Included = true },
            new { Key = "auth.mfa_enforcement", Module = "auth", Name = "MFA Enforcement", Included = true },
            new { Key = "configuration.tenant_settings", Module = "configuration", Name = "Tenant Settings", Included = true },
            new { Key = "roles.permission_management", Module = "roles", Name = "Permission Management", Included = true },
            new { Key = "notifications.email_delivery", Module = "notifications", Name = "Email Delivery", Included = true },
            new { Key = "notifications.in_app_delivery", Module = "notifications", Name = "In-App Delivery", Included = true },
            new { Key = "org.structure_management", Module = "org_structure", Name = "Structure Management", Included = true },
            
            // Core HR
            new { Key = "core_hr.employee_profiles", Module = "core_hr", Name = "Employee Profiles", Included = true },
            new { Key = "core_hr.employee_lifecycle", Module = "core_hr", Name = "Employee Lifecycle", Included = true },
            new { Key = "core_hr.onboarding", Module = "core_hr", Name = "Onboarding", Included = true },
            new { Key = "core_hr.offboarding", Module = "core_hr", Name = "Offboarding", Included = true },
            new { Key = "core_hr.dependents_contacts", Module = "core_hr", Name = "Dependents & Contacts", Included = true },
            new { Key = "core_hr.qualifications", Module = "core_hr", Name = "Qualifications", Included = true },
            new { Key = "core_hr.compensation", Module = "core_hr", Name = "Compensation", Included = true },

            // Time Off
            new { Key = "time_off.requests", Module = "time_off", Name = "Requests", Included = true },
            new { Key = "time_off.approvals", Module = "time_off", Name = "Approvals", Included = true },
            new { Key = "time_off.balances", Module = "time_off", Name = "Balances", Included = true },
            new { Key = "time_off.accrual_rules", Module = "time_off", Name = "Accrual Rules", Included = true },
            new { Key = "time_off.types", Module = "time_off", Name = "Types", Included = true },
            new { Key = "time_off.calendar_integration", Module = "time_off", Name = "Calendar Integration", Included = true },

            // Calendar
            new { Key = "calendar.company_calendar", Module = "calendar", Name = "Company Calendar", Included = true },
            new { Key = "calendar.holidays", Module = "calendar", Name = "Holidays", Included = true },
            new { Key = "calendar.time_off_visibility", Module = "calendar", Name = "Time Off Visibility", Included = true },
            new { Key = "calendar.event_sync", Module = "calendar", Name = "Event Sync", Included = true },

            // Time & Attendance
            new { Key = "time_attendance.work_schedules", Module = "time_attendance", Name = "Work Schedules", Included = true },

            // Activity Monitoring
            new { Key = "monitoring.activity_tracking", Module = "monitoring", Name = "Activity Tracking", Included = true },
            new { Key = "monitoring.app_usage", Module = "monitoring", Name = "App Usage", Included = true },
            new { Key = "monitoring.website_usage", Module = "monitoring", Name = "Website Usage", Included = true },
            new { Key = "monitoring.idle_detection", Module = "monitoring", Name = "Idle Detection", Included = true },
            new { Key = "monitoring.screenshot_on_demand", Module = "monitoring", Name = "Screenshot On Demand", Included = true },
            new { Key = "monitoring.app_allowlist", Module = "monitoring", Name = "App Allowlist", Included = true },
            new { Key = "monitoring.productivity_classification", Module = "monitoring", Name = "Productivity Classification", Included = true },
            new { Key = "monitoring.raw_data_processing", Module = "monitoring", Name = "Raw Data Processing", Included = true },
            new { Key = "monitoring.presence_sessions", Module = "monitoring", Name = "Presence Sessions", Included = true },
            new { Key = "monitoring.break_tracking", Module = "monitoring", Name = "Break Tracking", Included = true },
            new { Key = "monitoring.attendance_corrections", Module = "monitoring", Name = "Attendance Corrections", Included = true },
            new { Key = "monitoring.device_sessions", Module = "monitoring", Name = "Device Sessions", Included = true },
            new { Key = "monitoring.biometric_devices", Module = "monitoring", Name = "Biometric Devices", Included = true },

            // Identity Verification
            new { Key = "verification.identity_checks", Module = "verification", Name = "Identity Checks", Included = true },
            new { Key = "verification.face_match", Module = "verification", Name = "Face Match", Included = true },
            new { Key = "verification.verification_policies", Module = "verification", Name = "Verification Policies", Included = true },
            new { Key = "verification.manual_review", Module = "verification", Name = "Manual Review", Included = true },
            new { Key = "verification.photo_challenge", Module = "verification", Name = "Photo Challenge", Included = true },

            // Analytics
            new { Key = "analytics.daily_reports", Module = "analytics", Name = "Daily Reports", Included = true },
            new { Key = "analytics.monthly_reports", Module = "analytics", Name = "Monthly Reports", Included = true },
            new { Key = "analytics.monitoring_snapshots", Module = "analytics", Name = "Monitoring Snapshots", Included = true },
            new { Key = "analytics.productivity_dashboard", Module = "analytics", Name = "Productivity Dashboard", Included = true },
            new { Key = "analytics.data_export", Module = "analytics", Name = "Data Export", Included = true },
            new { Key = "analytics.scheduled_reports", Module = "analytics", Name = "Scheduled Reports", Included = true },

            // Work Management
            new { Key = "work_management.projects", Module = "work_management", Name = "Projects", Included = true },
            new { Key = "work_management.tasks", Module = "work_management", Name = "Tasks", Included = true },
            new { Key = "work_management.sprints", Module = "work_management", Name = "Sprints", Included = true },
            new { Key = "work_management.boards", Module = "work_management", Name = "Boards", Included = true },
            new { Key = "work_management.okrs", Module = "work_management", Name = "OKRs", Included = true },
            new { Key = "work_management.roadmaps", Module = "work_management", Name = "Roadmaps", Included = true },
            new { Key = "work_management.time_tracking", Module = "work_management", Name = "Time Tracking", Included = true },
            new { Key = "work_management.resource_planning", Module = "work_management", Name = "Resource Planning", Included = true },
            new { Key = "work_management.work_analytics", Module = "work_management", Name = "Work Analytics", Included = true },
            new { Key = "work_management.github_integration", Module = "work_management", Name = "GitHub Integration", Included = true },

            // Integrations
            new { Key = "integrations.microsoft_teams", Module = "integrations", Name = "Microsoft Teams", Included = true },
            new { Key = "integrations.github", Module = "integrations", Name = "GitHub", Included = true },
            new { Key = "integrations.google_workspace", Module = "integrations", Name = "Google Workspace", Included = true },
            new { Key = "integrations.webhooks", Module = "integrations", Name = "Webhooks", Included = true },
            new { Key = "integrations.api_access", Module = "integrations", Name = "API Access", Included = true }
        };

        var existingFeatures = await db.ModuleFeatures.ToListAsync(ct);
        var existingByKey = existingFeatures.ToDictionary(f => f.FeatureKey, StringComparer.Ordinal);

        foreach (var def in featuresToSeed)
        {
            if (existingByKey.TryGetValue(def.Key, out var feature))
            {
                feature.Name = def.Name;
                feature.IsDefaultIncluded = def.Included;
            }
            else
            {
                db.ModuleFeatures.Add(new ModuleFeature
                {
                    FeatureKey = def.Key,
                    ModuleKey = def.Module,
                    Name = def.Name,
                    IsDefaultIncluded = def.Included,
                    IsActive = true
                });
            }
        }
    }

    private static async Task SeedPermissionOwnershipAsync(ApplicationDbContext db, CancellationToken ct)
    {
        // Map owner_permissions strictly from phase-1-feature-permission-map.md
        var ownershipToSeed = new[]
        {
            // auth
            new { Module = "auth", Perm = "users:manage" },

            // configuration
            new { Module = "configuration", Perm = "settings:read" },
            new { Module = "configuration", Perm = "settings:admin" },

            // roles
            new { Module = "roles", Perm = "roles:manage" },

            // notifications
            new { Module = "notifications", Perm = "notifications:manage" },
            new { Module = "notifications", Perm = "settings:notifications" },

            // org_structure
            new { Module = "org_structure", Perm = "org:read" },
            new { Module = "org_structure", Perm = "org:manage" },

            // core_hr
            new { Module = "core_hr", Perm = "employees:read" },
            new { Module = "core_hr", Perm = "employees:write" },
            new { Module = "core_hr", Perm = "employees:delete" },

            // time_off
            new { Module = "time_off", Perm = "time_off:read" },
            new { Module = "time_off", Perm = "time_off:create" },
            new { Module = "time_off", Perm = "time_off:approve" },
            new { Module = "time_off", Perm = "time_off:manage" },

            // calendar
            new { Module = "calendar", Perm = "calendar:read" },
            new { Module = "calendar", Perm = "calendar:write" },
            new { Module = "calendar", Perm = "calendar:admin" },

            // time_attendance
            new { Module = "time_attendance", Perm = "attendance:read" },
            new { Module = "time_attendance", Perm = "attendance:write" },
            new { Module = "time_attendance", Perm = "attendance:approve" },

            // monitoring
            new { Module = "monitoring", Perm = "monitoring:read" },
            new { Module = "monitoring", Perm = "monitoring:configure" },

            // verification
            new { Module = "verification", Perm = "verification:view" },
            new { Module = "verification", Perm = "verification:review" },
            new { Module = "verification", Perm = "verification:configure" },

            // analytics
            new { Module = "analytics", Perm = "analytics:view" },
            new { Module = "analytics", Perm = "analytics:export" },
            new { Module = "analytics", Perm = "analytics:write" },
            new { Module = "analytics", Perm = "reports:create" },

            // work_management
            new { Module = "work_management", Perm = "projects:read" },
            new { Module = "work_management", Perm = "projects:write" },
            new { Module = "work_management", Perm = "projects:create" },
            new { Module = "work_management", Perm = "tasks:read" },
            new { Module = "work_management", Perm = "tasks:write" },
            new { Module = "work_management", Perm = "tasks:approve" },
            new { Module = "work_management", Perm = "tasks:delete" },
            new { Module = "work_management", Perm = "sprints:read" },
            new { Module = "work_management", Perm = "sprints:manage" },
            new { Module = "work_management", Perm = "okr:read" },
            new { Module = "work_management", Perm = "okr:write" },
            new { Module = "work_management", Perm = "roadmaps:read" },
            new { Module = "work_management", Perm = "roadmaps:write" },
            new { Module = "work_management", Perm = "time:read" },
            new { Module = "work_management", Perm = "time:write" },
            new { Module = "work_management", Perm = "time:approve" },
            new { Module = "work_management", Perm = "resources:read" },
            new { Module = "work_management", Perm = "resources:manage" },

            // integrations
            new { Module = "integrations", Perm = "integrations:manage" }
        };

        var existingPermissions = await db.Permissions.Select(p => p.Code).ToListAsync(ct);
        var permSet = new HashSet<string>(existingPermissions, StringComparer.Ordinal);

        var existingOwnerships = await db.ModulePermissionOwnerships.ToListAsync(ct);
        var existingByCode = existingOwnerships.ToDictionary(o => o.PermissionCode, StringComparer.Ordinal);

        foreach (var def in ownershipToSeed)
        {
            if (!permSet.Contains(def.Perm))
            {
                // Note: The missing permissions (e.g. time_off:* vs leave:*) are expected until the tenant catalog maps them accurately. 
                // We do not throw to allow partial seeding. The missing permissions gap report is generated by the implementer offline, not at runtime.
                continue;
            }

            if (existingByCode.TryGetValue(def.Perm, out var ownership))
            {
                ownership.ModuleKey = def.Module;
            }
            else
            {
                db.ModulePermissionOwnerships.Add(new ModulePermissionOwnership
                {
                    ModuleKey = def.Module,
                    PermissionCode = def.Perm,
                    IsDefaultPermission = true
                });
            }
        }
    }
}
