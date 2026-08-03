using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public class PermissionSeeder : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<PermissionSeeder> _logger;

    public PermissionSeeder(IServiceProvider services, ILogger<PermissionSeeder> logger)
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

            await SeedPermissionsAsync(db, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Permission seeder could not run because the database or migrated schema is unavailable. " +
                "Apply migrations explicitly with setup-local-db.ps1 -RunMigrations. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedPermissionsAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var defined = GetAllPermissions();
        var existingRows = await db.Permissions.ToListAsync(ct);
        var existing = existingRows
            .Select(p => p.Code)
            .ToHashSet(StringComparer.Ordinal);

        var definedByCode = defined.ToDictionary(p => p.Code, StringComparer.Ordinal);
        var updated = 0;
        foreach (var row in existingRows)
        {
            if (!definedByCode.TryGetValue(row.Code, out var expected))
                continue;

            if (row.Module == expected.Module && row.Description == expected.Description)
                continue;

            row.Module = expected.Module;
            row.Description = expected.Description;
            updated++;
        }

        var toAdd = defined
            .Where(p => !existing.Contains(p.Code))
            .ToList();

        if (toAdd.Count == 0 && updated == 0)
        {
            _logger.LogInformation("Permissions already seeded â€” skipping.");
            return;
        }

        await db.Permissions.AddRangeAsync(toAdd, ct);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} new permissions and updated {UpdatedCount} existing permissions.", toAdd.Count, updated);
    }

    private static List<Permission> GetAllPermissions() =>
    [
        // System
        Perm("*", "Super Admin bypass â€” all access.", "system"),

        // Employees
        Perm("employees:read", "View all employees in scope.", "core_hr"),
        Perm("employees:read-team", "View direct reports only.", "core_hr"),
        Perm("employees:write", "Create, update employees.", "core_hr"),
        Perm("employees:delete", "Delete employee records.", "core_hr"),

        // Organization
        Perm("org:read", "View org structure, departments, hierarchy.", "org"),
        Perm("org:manage", "Create and edit org structure, departments.", "org"),

        // Leave (Legacy compatibility alias - to be migrated to time_off. Not used for Module Catalog ownership)
        Perm("leave:read", "View leave records for all employees in scope.", "leave"),
        Perm("leave:read-team", "View leave records for direct reports only.", "leave"),
        Perm("leave:create", "Apply for leave on behalf of others.", "leave"),
        Perm("leave:approve", "Approve or reject leave requests.", "leave"),
        Perm("leave:manage", "Manage leave types, policies, balances.", "leave"),

        // Time Off (Canonical Developer Platform keys)
        Perm("time_off:read", "View time off records for all employees in scope.", "time_off"),
        Perm("time_off:create", "Apply for time off on behalf of others.", "time_off"),
        Perm("time_off:approve", "Approve or reject time off requests.", "time_off"),
        Perm("time_off:manage", "Manage time off types, policies, balances.", "time_off"),

        // Calendar
        Perm("calendar:read", "View company and team calendars.", "calendar"),

        // Monitoring
        Perm("monitoring:read", "View monitoring data.", "monitoring"),

        // Attendance
        Perm("attendance:read", "View attendance records for all employees in scope.", "core_hr"),
        Perm("attendance:read-team", "View attendance records for direct reports only.", "core_hr"),
        Perm("attendance:approve", "Approve overtime and attendance corrections.", "core_hr"),
        Perm("attendance:write", "Correct attendance records for employees in scope.", "core_hr"),

        // Payroll
        Perm("payroll:read", "View payroll records and salary data.", "payroll"),
        Perm("payroll:write", "Edit payroll records.", "payroll"),
        Perm("payroll:approve", "Approve payroll runs.", "payroll"),
        Perm("payroll:run", "Execute payroll processing.", "payroll"),

        // Performance
        Perm("performance:read", "View performance records for all employees in scope.", "performance"),
        Perm("performance:read-team", "View performance records for direct reports only.", "performance"),
        Perm("performance:write", "Submit and edit performance reviews.", "performance"),
        Perm("performance:manage", "Manage review cycles, templates, goals.", "performance"),

        // Skills
        Perm("skills:read", "View skills profiles across employees.", "skills"),
        Perm("skills:write", "Update own skills profile.", "skills"),
        Perm("skills:write-team", "Update skills for direct reports.", "skills"),
        Perm("skills:validate", "Validate or endorse skills for employees.", "skills"),
        Perm("skills:manage", "Manage skill library and categories.", "skills"),

        // Expense
        Perm("expense:read", "View expense records in scope.", "expense"),
        Perm("expense:create", "Submit expense claims.", "expense"),
        Perm("expense:approve", "Approve or reject expense claims.", "expense"),
        Perm("expense:manage", "Manage expense categories, policies, limits.", "expense"),

        // Calendar
        Perm("calendar:write", "Create and manage calendar events for others.", "calendar"),
        Perm("calendar:admin", "Manage country holiday sync, legal-entity calendar overrides, and calendar integrations.", "calendar"),

        // Notifications
        Perm("notifications:manage", "Manage notification templates and delivery settings.", "notifications"),

        // Settings
        Perm("settings:read", "View any settings section (read-only).", "configuration"),
        Perm("settings:admin", "Manage core system settings.", "configuration"),
        Perm("settings:billing", "Manage subscription, plan, and payment methods.", "configuration"),
        Perm("settings:branding", "Manage company logo, colors, and custom domain.", "configuration"),
        Perm("settings:integrations", "Connect or disconnect tenant-wide app integrations.", "configuration"),
        Perm("settings:notifications", "Manage notification templates and delivery channels.", "configuration"),
        Perm("settings:alerts", "Configure alert thresholds and escalation rules.", "configuration"),
        Perm("settings:system", "Manage system-level settings â€” audit config, data retention policies.", "configuration"),
        Perm("settings:device", "View biometric device connection status.", "configuration"),
        Perm("settings:device:configure", "Add, remove, and configure biometric device integrations.", "configuration"),

        // Users & Roles
        Perm("users:read", "View user accounts.", "auth"),
        Perm("users:manage", "Create, suspend, and manage user accounts.", "auth"),
        Perm("roles:read", "View roles and their permission sets.", "roles"),
        Perm("roles:manage", "Create and edit roles, assign permissions.", "roles"),

        // Billing
        Perm("billing:read", "View billing history and invoices.", "configuration"),
        Perm("billing:manage", "Manage billing and subscription changes.", "configuration"),

        // Integrations
        Perm(
            "integrations:manage",
            "Manage tenant-wide integration enablement, configuration, and other users' connections.",
            "integrations"),

        // Analytics
        Perm("analytics:read", "View analytics data.", "analytics"),
        Perm("analytics:view", "Access analytics dashboards.", "analytics"),
        Perm("analytics:export", "Export analytics reports.", "analytics"),
        Perm("analytics:write", "Create and save custom analytics views.", "analytics"),

        // Monitoring
        Perm("monitoring:configure", "Enable/disable monitoring features, set employee overrides.", "monitoring"),

        // Exceptions
        Perm("exceptions:view", "View exception alerts.", "exceptions"),
        Perm("exceptions:acknowledge", "Acknowledge and resolve exception alerts.", "exceptions"),
        Perm("exceptions:manage", "Manage exception rules and thresholds.", "exceptions"),

        // Verification
        Perm("verification:view", "View identity verification results.", "verification"),
        Perm("verification:review", "Escalate verification cases.", "verification"),
        Perm("verification:configure", "Configure verification settings and rules.", "verification"),

        // Workforce Intelligence
        Perm("workforce:view", "View workforce intelligence data and reports.", "workforce"),
        Perm("workforce:manage", "Manage workforce intelligence settings.", "workforce"),

        // Agent Gateway
        Perm("agent:command", "Send commands to agents.", "monitoring"),
        Perm("agent:manage", "Manage agent configurations.", "monitoring"),
        Perm("agent:register", "Register new agents.", "monitoring"),
        Perm("agent:view-health", "View agent health and status.", "monitoring"),

        // Documents
        Perm("documents:read", "View documents.", "documents"),
        Perm("documents:write", "Upload and edit documents.", "documents"),
        Perm("documents:approve", "Approve document submissions.", "documents"),
        Perm("documents:manage", "Manage document categories and policies.", "documents"),
        Perm("documents:admin", "Full document administration including deletion and access control.", "documents"),

        // Grievance
        Perm("grievance:read", "View grievance cases.", "grievance"),
        Perm("grievance:write", "Submit grievance cases.", "grievance"),
        Perm("grievance:manage", "Manage and resolve grievance cases.", "grievance"),

        // Reports
        Perm("reports:read", "View reports.", "reports"),
        Perm("reports:create", "Create and run reports.", "reports"),

        // Chat
        Perm("chat:read", "Read chat messages.", "chat"),
        Perm("chat:write", "Send chat messages.", "chat"),
        Perm("chat:manage", "Manage channels, moderate messages.", "chat"),

        // Tasks
        Perm("tasks:read", "View tasks.", "work_management"),
        Perm("tasks:write", "Create and edit tasks.", "work_management"),
        Perm("tasks:approve", "Approve task completions.", "work_management"),
        Perm("tasks:delete", "Delete tasks.", "work_management"),

        // Time Tracking
        Perm("time:read", "View time logs.", "work_management"),
        Perm("time:write", "Log and edit time entries.", "work_management"),
        Perm("time:approve", "Approve time submissions.", "work_management"),

        // Projects
        Perm("projects:read", "View projects.", "work_management"),
        Perm("projects:write", "Edit project details.", "work_management"),
        Perm("projects:create", "Create new projects.", "work_management"),

        // Work Management
        Perm("okr:read", "View OKRs and goals.", "work_management"),
        Perm("okr:write", "Create and update OKRs.", "work_management"),
        Perm("wiki:read", "View wiki pages.", "work_management"),
        Perm("wiki:write", "Create and edit wiki pages.", "work_management"),
        Perm("sprints:read", "View sprints.", "work_management"),
        Perm("sprints:manage", "Create and manage sprints.", "work_management"),
        Perm("workspaces:read", "View workspaces.", "work_management"),
        Perm("workspaces:create", "Create new workspaces.", "work_management"),
        Perm("workspaces:manage", "Manage workspace settings and members.", "work_management"),
        Perm("resources:read", "View resource allocations.", "work_management"),
        Perm("resources:manage", "Manage resource planning.", "work_management"),
        Perm("roadmaps:read", "View roadmaps.", "work_management"),
        Perm("roadmaps:write", "Create and edit roadmaps.", "work_management"),

        // Work Management — Projects (Foundation slice additions)
        Perm("members:read", "View project members.", "work_management"),
        Perm("members:manage", "Activate, deactivate, or remove project members.", "work_management"),
        Perm("invitations:manage", "Send and cancel project/objective invitations.", "work_management"),
        Perm("invitations:respond", "Accept or decline a project/objective invitation.", "work_management"),
        Perm("versions:write", "Create and change project version status.", "work_management"),
        Perm("labels:manage", "Create and edit project labels.", "work_management"),
    ];

    private static Permission Perm(string code, string description, string module) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Description = description,
        Module = module
    };
}

