namespace ONEVO.Application.Common.Authorization;

/// <summary>How a tenant permission code reaches a user.</summary>
public enum PermissionKind
{
    /// <summary>Has a row in the <c>permissions</c> table and can be given through a role or an override.</summary>
    Assignable,

    /// <summary>Given automatically for an active module (ModuleAutoGrants). No <c>permissions</c> row, so an admin cannot tick it.</summary>
    AutoGranted,

    /// <summary>Added by the resolver when the user holds a trigger code (DerivedPermissions). No <c>permissions</c> row.</summary>
    Derived,

    /// <summary>Referenced in code but never seeded and never enforced. Listed so the gap is visible; task 33 decides its fate.</summary>
    NotSeeded,

    /// <summary>
    /// First retirement stage. The row stays with retired_at set: the code stops working and leaves the role
    /// screen, but its grants are kept, so changing the entry back to Assignable restores them. Move it to
    /// <see cref="Retired"/> in a later release.
    /// </summary>
    SoftRetired,

    /// <summary>
    /// Removed. The sync deletes its row, and by cascade its grants, from existing databases. While any role or
    /// override still holds it, the sync refuses unless the definition sets <c>DropGrants</c>.
    /// </summary>
    Retired,
}

/// <summary>
/// One tenant permission code. <see cref="Module"/> and <see cref="Description"/> are what the seeder writes
/// to the <c>permissions</c> row for <see cref="PermissionKind.Assignable"/> codes. For a retired code the
/// description is the reason, and <see cref="DropGrants"/> says losing its remaining grants is intended.
/// Renames and default grants for new codes are declared in <see cref="PermissionCatalog.Renames"/> and
/// <see cref="PermissionCatalog.DefaultGrants"/>.
/// </summary>
public sealed record PermissionDefinition(
    string Code, string Module, string Description, PermissionKind Kind, bool DropGrants = false)
{
    public bool IsRetired => Kind is PermissionKind.SoftRetired or PermissionKind.Retired;
}

/// <summary>
/// The single source of tenant permission codes. Attributes, <c>HasPermission</c> calls, seeders, auto-grants
/// and derived codes all use these constants, and <c>PermissionSeeder</c> writes <see cref="Seeded"/> to the
/// <c>permissions</c> table. Architecture tests fail on a permission string literal anywhere else in src, on a
/// constant without a definition, and on duplicate codes. Developer Platform codes are separate, in
/// <c>PlatformPermissionCatalog</c>.
/// </summary>
public static class PermissionCatalog
{
    /// <summary>Super Admin bypass: a user holding it passes every tenant permission check.</summary>
    public const string SuperAdmin = "*";

    public static class Employees
    {
        public const string Write = "employees:write";
        public const string Offboard = "employees:offboard";
        public const string ReadSensitive = "employees:read:sensitive";
        public const string Delete = "employees:delete";
        public const string ReadOwn = "employees:read-own";
    }

    public static class Checklists
    {
        public const string Manage = "checklists:manage";
    }

    public static class Org
    {
        public const string Read = "org:read";
        public const string Manage = "org:manage";
    }

    public static class LegalEntity
    {
        public const string Create = "legal_entity:create";
        public const string Read = "legal_entity:read";
        public const string Update = "legal_entity:update";
        public const string Delete = "legal_entity:delete";
    }

    public static class Leave
    {
        public const string Read = "leave:read";
        public const string ReadTeam = "leave:read-team";
        public const string Create = "leave:create";
        public const string Approve = "leave:approve";
        public const string Manage = "leave:manage";
        public const string ReadOwn = "leave:read-own";
    }

    public static class TimeOff
    {
        public const string Read = "time_off:read";
        public const string Create = "time_off:create";
        public const string Approve = "time_off:approve";
        public const string Manage = "time_off:manage";
    }

    public static class Calendar
    {
        public const string Read = "calendar:read";
        public const string Write = "calendar:write";
        public const string Admin = "calendar:admin";
    }

    public static class Monitoring
    {
        public const string Read = "monitoring:read";
        public const string Configure = "monitoring:configure";
        public const string ScreenshotsRequest = "monitoring:screenshots:request";
        public const string AlertsRead = "monitoring:alerts:read";
        public const string AlertsResolve = "monitoring:alerts:resolve";
    }

    public static class Activity
    {
        public const string ReadSelf = "activity:read:self";
    }

    public static class Attendance
    {
        public const string Read = "attendance:read";
        public const string Approve = "attendance:approve";
        public const string Write = "attendance:write";
        public const string ReadOwn = "attendance:read-own";
        public const string WriteOwn = "attendance:write-own";
    }

    public static class Payroll
    {
        public const string Read = "payroll:read";
        public const string Write = "payroll:write";
        public const string Approve = "payroll:approve";
        public const string Run = "payroll:run";
    }

    public static class Performance
    {
        public const string Read = "performance:read";
        public const string ReadTeam = "performance:read-team";
        public const string Write = "performance:write";
        public const string Manage = "performance:manage";
    }

    public static class Skills
    {
        public const string Read = "skills:read";
        public const string Write = "skills:write";
        public const string WriteTeam = "skills:write-team";
        public const string Validate = "skills:validate";
        public const string Manage = "skills:manage";
    }

    public static class Expense
    {
        public const string Read = "expense:read";
        public const string Create = "expense:create";
        public const string Approve = "expense:approve";
        public const string Manage = "expense:manage";
    }

    public static class Notifications
    {
        public const string Manage = "notifications:manage";
        public const string Read = "notifications:read";
    }

    public static class Settings
    {
        public const string Device = "settings:device";
        public const string DeviceConfigure = "settings:device:configure";
    }

    public static class Users
    {
        public const string Read = "users:read";
        public const string Manage = "users:manage";
    }

    public static class Roles
    {
        public const string Read = "roles:read";
        public const string Manage = "roles:manage";
    }

    public static class Billing
    {
        public const string Read = "billing:read";
        public const string Manage = "billing:manage";
    }

    public static class Support
    {
        public const string Manage = "support:manage";
    }

    public static class Integrations
    {
        public const string Manage = "integrations:manage";
    }

    public static class Analytics
    {
        public const string Read = "analytics:read";
        public const string View = "analytics:view";
        public const string Export = "analytics:export";
        public const string Write = "analytics:write";
    }

    public static class Exceptions
    {
        public const string View = "exceptions:view";
        public const string Acknowledge = "exceptions:acknowledge";
        public const string Manage = "exceptions:manage";
    }

    public static class Verification
    {
        public const string View = "verification:view";
        public const string Review = "verification:review";
        public const string Configure = "verification:configure";
    }

    public static class Workforce
    {
        public const string View = "workforce:view";
        public const string Manage = "workforce:manage";
        public const string Dashboard = "workforce:dashboard";
    }

    public static class Documents
    {
        public const string Read = "documents:read";
        public const string Write = "documents:write";
        public const string Approve = "documents:approve";
        public const string Manage = "documents:manage";
        public const string Admin = "documents:admin";
    }

    public static class Grievance
    {
        public const string Read = "grievance:read";
        public const string Write = "grievance:write";
        public const string Manage = "grievance:manage";
    }

    public static class Reports
    {
        public const string Read = "reports:read";
        public const string Create = "reports:create";
    }

    public static class Chat
    {
        public const string Read = "chat:read";
        public const string Write = "chat:write";
        public const string Manage = "chat:manage";
    }

    public static class Projects
    {
        public const string Create = "projects:create";
    }

    public static class Inbox
    {
        public const string Read = "inbox:read";
    }

    public static class Workflows
    {
        public const string Execute = "workflows:execute";
    }

    /// <summary>
    /// Retired codes. Do not check these: no user can hold them. They are kept so the seeder can remove
    /// their rows from existing databases.
    /// </summary>
    public static class Retired
    {
        public const string AgentCommand = "agent:command";
        public const string AgentManage = "agent:manage";
        public const string AgentRegister = "agent:register";
        public const string AgentViewHealth = "agent:view-health";
        public const string ProjectsAccess = "projects:access";
        public const string ProjectsRead = "projects:read";
        public const string TasksRead = "tasks:read";
        public const string TasksReadOwn = "tasks:read-own";
        public const string TasksWrite = "tasks:write";
        public const string TasksApprove = "tasks:approve";
        public const string TasksDelete = "tasks:delete";
        public const string TimeRead = "time:read";
        public const string TimeWrite = "time:write";
        public const string TimeApprove = "time:approve";
        public const string OkrRead = "okr:read";
        public const string OkrWrite = "okr:write";
        public const string WikiRead = "wiki:read";
        public const string WikiWrite = "wiki:write";
        public const string SprintsRead = "sprints:read";
        public const string SprintsManage = "sprints:manage";
        public const string WorkspacesRead = "workspaces:read";
        public const string WorkspacesCreate = "workspaces:create";
        public const string WorkspacesManage = "workspaces:manage";
        public const string ResourcesRead = "resources:read";
        public const string ResourcesManage = "resources:manage";
        public const string RoadmapsRead = "roadmaps:read";
        public const string RoadmapsWrite = "roadmaps:write";
        public const string SettingsRead = "settings:read";
        public const string SettingsAdmin = "settings:admin";
        public const string SettingsBilling = "settings:billing";
        public const string SettingsBranding = "settings:branding";
        public const string SettingsIntegrations = "settings:integrations";
        public const string SettingsAlerts = "settings:alerts";
        public const string SettingsSystem = "settings:system";
        public const string EmployeesRead = "employees:read";
        public const string EmployeesReadTeam = "employees:read-team";
        public const string AttendanceReadTeam = "attendance:read-team";
        public const string InvitationsManage = "invitations:manage";
    }

    private static readonly PermissionDefinition[] Definitions =
    [
        // System
        Assignable(SuperAdmin, "system", "Super Admin bypass â€” all access."),

        // Employees
        Assignable(Employees.Write, "core_hr", "Create, update employees."),
        Assignable(Employees.Offboard, "core_hr", "Start, cancel, or complete an employee's offboarding."),
        Assignable(Employees.ReadSensitive, "core_hr", "View sensitive employee data (bank details) on the employee detail screen."),
        Assignable(Employees.Delete, "core_hr", "Delete employee records."),
        Assignable(Checklists.Manage, "core_hr", "Create, edit, and archive onboarding and offboarding checklist templates."),

        // Organization
        Assignable(Org.Read, "org_structure", "View org structure, departments, hierarchy."),
        Assignable(Org.Manage, "org_structure", "Create and edit org structure, departments."),
        Assignable(LegalEntity.Create, "org_structure", "Create a legal entity (company) inside the tenant."),
        Assignable(LegalEntity.Read, "org_structure", "View a legal entity's general settings (read-only)."),
        Assignable(LegalEntity.Update, "org_structure", "Edit a legal entity's general settings."),
        Assignable(LegalEntity.Delete, "org_structure", "Deactivate (soft-delete) a legal entity."),

        // Leave (Legacy compatibility alias - to be migrated to time_off. Not used for Module Catalog ownership)
        Assignable(Leave.Read, "leave", "View leave records for all employees in scope."),
        Assignable(Leave.ReadTeam, "leave", "View leave records for direct reports only."),
        Assignable(Leave.Create, "leave", "Apply for leave on behalf of others."),
        Assignable(Leave.Approve, "leave", "Approve or reject leave requests."),
        Assignable(Leave.Manage, "leave", "Manage leave types, policies, balances."),

        // Time Off (Canonical Developer Platform keys)
        Assignable(TimeOff.Read, "time_off", "View time off records for all employees in scope."),
        Assignable(TimeOff.Create, "time_off", "Apply for time off on behalf of others."),
        Assignable(TimeOff.Approve, "time_off", "Approve or reject time off requests."),
        Assignable(TimeOff.Manage, "time_off", "Manage time off types, policies, balances."),

        // Calendar
        Assignable(Calendar.Read, "calendar", "View company and team calendars."),

        // Monitoring
        Assignable(Monitoring.Read, "activity_monitoring", "View monitoring data."),
        Assignable(Activity.ReadSelf, "activity_monitoring", "View your own activity timeline."),

        // Attendance
        Assignable(Attendance.Read, "core_hr", "View attendance records for all employees in scope."),
        Assignable(Attendance.Approve, "core_hr", "Approve requests and alerts for your team and escalated records: attendance corrections, location, work mode and device changes, identity verification alerts."),
        Assignable(Attendance.Write, "core_hr", "Configure clock-in policy and work modes."),

        // Payroll
        Assignable(Payroll.Read, "payroll", "View payroll records and salary data."),
        Assignable(Payroll.Write, "payroll", "Edit payroll records."),
        Assignable(Payroll.Approve, "payroll", "Approve payroll runs."),
        Assignable(Payroll.Run, "payroll", "Execute payroll processing."),

        // Performance
        Assignable(Performance.Read, "performance", "View performance records for all employees in scope."),
        Assignable(Performance.ReadTeam, "performance", "View performance records for direct reports only."),
        Assignable(Performance.Write, "performance", "Submit and edit performance reviews."),
        Assignable(Performance.Manage, "performance", "Manage review cycles, templates, goals."),

        // Skills
        Assignable(Skills.Read, "skills", "View skills profiles across employees."),
        Assignable(Skills.Write, "skills", "Update own skills profile."),
        Assignable(Skills.WriteTeam, "skills", "Update skills for direct reports."),
        Assignable(Skills.Validate, "skills", "Validate or endorse skills for employees."),
        Assignable(Skills.Manage, "skills", "Manage skill library and categories."),

        // Expense
        Assignable(Expense.Read, "expense", "View expense records in scope."),
        Assignable(Expense.Create, "expense", "Submit expense claims."),
        Assignable(Expense.Approve, "expense", "Approve or reject expense claims."),
        Assignable(Expense.Manage, "expense", "Manage expense categories, policies, limits."),

        // Calendar
        Assignable(Calendar.Write, "calendar", "Create and manage calendar events for others."),
        Assignable(Calendar.Admin, "calendar", "Manage country holiday sync, legal-entity calendar overrides, and calendar integrations."),

        // Notifications
        Assignable(Notifications.Manage, "notifications", "Manage notification templates and delivery settings."),

        // Settings
        Assignable(Settings.Device, "configuration", "View biometric device connection status."),
        Assignable(Settings.DeviceConfigure, "configuration", "Add, remove, and configure biometric device integrations."),

        // Users & Roles
        Assignable(Users.Read, "auth", "View user accounts."),
        Assignable(Users.Manage, "auth", "Create, suspend, and manage user accounts."),
        Assignable(Roles.Read, "roles", "View roles and their permission sets."),
        Assignable(Roles.Manage, "roles", "Create and edit roles, assign permissions."),

        // Billing
        Assignable(Billing.Read, "configuration", "View billing history and invoices."),
        Assignable(Billing.Manage, "configuration", "Manage billing and subscription changes."),

        // Support (Crownus support tickets raised by the tenant)
        Assignable(Support.Manage, "configuration", "Raise and follow up support tickets with Crownus support."),

        // Integrations
        Assignable(Integrations.Manage, "integrations", "Manage tenant-wide integration enablement, configuration, and other users' connections."),

        // Analytics
        Assignable(Analytics.Read, "analytics", "View analytics data."),
        Assignable(Analytics.View, "analytics", "Access analytics dashboards."),
        Assignable(Analytics.Export, "analytics", "Export analytics reports."),
        Assignable(Analytics.Write, "analytics", "Create and save custom analytics views."),

        // Monitoring
        Assignable(Monitoring.Configure, "activity_monitoring", "Enable/disable monitoring features, set employee overrides."),
        Assignable(Monitoring.ScreenshotsRequest, "activity_monitoring", "Request an on-demand screenshot from an active monitored device."),

        // Exceptions
        Assignable(Exceptions.View, "exceptions", "View exception alerts."),
        Assignable(Exceptions.Acknowledge, "exceptions", "Acknowledge and resolve exception alerts."),
        Assignable(Exceptions.Manage, "exceptions", "Manage exception rules and thresholds."),

        // Verification
        Assignable(Verification.View, "verification", "View identity verification results."),
        Assignable(Verification.Review, "verification", "Escalate verification cases."),
        Assignable(Verification.Configure, "verification", "Configure verification settings and rules."),

        // Workforce Intelligence
        Assignable(Workforce.View, "workforce", "View workforce intelligence data and reports."),
        Assignable(Workforce.Manage, "workforce", "Manage workforce intelligence settings."),

        // Documents
        Assignable(Documents.Read, "documents", "View documents."),
        Assignable(Documents.Write, "documents", "Upload and edit documents."),
        Assignable(Documents.Approve, "documents", "Approve document submissions."),
        Assignable(Documents.Manage, "documents", "Manage document categories and policies."),
        Assignable(Documents.Admin, "documents", "Full document administration including deletion and access control."),

        // Grievance
        Assignable(Grievance.Read, "grievance", "View grievance cases."),
        Assignable(Grievance.Write, "grievance", "Submit grievance cases."),
        Assignable(Grievance.Manage, "grievance", "Manage and resolve grievance cases."),

        // Reports
        Assignable(Reports.Read, "reports", "View reports."),
        Assignable(Reports.Create, "reports", "Create and run reports."),

        // Chat
        Assignable(Chat.Read, "chat", "Read chat messages."),
        Assignable(Chat.Write, "chat", "Send chat messages."),
        Assignable(Chat.Manage, "chat", "Manage channels, moderate messages."),

        // Work Management uses module entitlement plus contextual relationships for ordinary
        // access. Project creation remains an explicitly delegable elevated operation.
        Assignable(Projects.Create, "projects", "Create projects where the caller has local creation authority."),

        // Granted automatically for an active module (see ModuleAutoGrants). calendar:read and
        // activity:read:self are auto-granted too but keep their assignable rows above.
        Definition(Employees.ReadOwn, "core_hr", "View your own employee record. Granted automatically with Core HR.", PermissionKind.AutoGranted),
        Definition(Attendance.ReadOwn, "core_hr", "View your own attendance. Granted automatically with Core HR.", PermissionKind.AutoGranted),
        Definition(Attendance.WriteOwn, "core_hr", "Record your own attendance. Granted automatically with Core HR.", PermissionKind.AutoGranted),
        Definition(Leave.ReadOwn, "leave", "View and request your own leave. Granted automatically with Leave.", PermissionKind.AutoGranted),
        Definition(Workforce.Dashboard, "workforce", "View your own workforce dashboard. Granted automatically with Workforce Intelligence.", PermissionKind.AutoGranted),

        // Added by PermissionResolver from the DerivedPermissions trigger lists.
        Definition(Inbox.Read, "inbox", "Open the Inbox. Derived from holding any Inbox trigger code (DerivedPermissions.InboxTriggers).", PermissionKind.Derived),
        Definition(Notifications.Read, "notifications", "Read notifications. Derived from holding any notification trigger code (DerivedPermissions.NotificationTriggers).", PermissionKind.Derived),

        // Trigger codes in DerivedPermissions that have no permissions row (review problem 10).
        Definition(Monitoring.AlertsRead, "activity_monitoring", "Trigger code in DerivedPermissions only. No permissions row and no endpoint checks it (task 32 mismatch report).", PermissionKind.NotSeeded),
        Definition(Monitoring.AlertsResolve, "activity_monitoring", "Trigger code in DerivedPermissions only. No permissions row and no endpoint checks it (task 32 mismatch report).", PermissionKind.NotSeeded),
        Definition(Workflows.Execute, "workflows", "Trigger code in DerivedPermissions only. No permissions row and no endpoint checks it (task 32 mismatch report).", PermissionKind.NotSeeded),

        // Retired (removed). The sync deletes these rows from existing databases.
        RetiredCode(PermissionCatalog.Retired.AgentCommand),
        RetiredCode(PermissionCatalog.Retired.AgentManage),
        RetiredCode(PermissionCatalog.Retired.AgentRegister),
        RetiredCode(PermissionCatalog.Retired.AgentViewHealth),
        RetiredCode(PermissionCatalog.Retired.ProjectsAccess),
        RetiredCode(PermissionCatalog.Retired.ProjectsRead),
        RetiredCode(PermissionCatalog.Retired.TasksRead),
        RetiredCode(PermissionCatalog.Retired.TasksReadOwn),
        RetiredCode(PermissionCatalog.Retired.TasksWrite),
        RetiredCode(PermissionCatalog.Retired.TasksApprove),
        RetiredCode(PermissionCatalog.Retired.TasksDelete),
        RetiredCode(PermissionCatalog.Retired.TimeRead),
        RetiredCode(PermissionCatalog.Retired.TimeWrite),
        RetiredCode(PermissionCatalog.Retired.TimeApprove),
        RetiredCode(PermissionCatalog.Retired.OkrRead),
        RetiredCode(PermissionCatalog.Retired.OkrWrite),
        RetiredCode(PermissionCatalog.Retired.WikiRead),
        RetiredCode(PermissionCatalog.Retired.WikiWrite),
        RetiredCode(PermissionCatalog.Retired.SprintsRead),
        RetiredCode(PermissionCatalog.Retired.SprintsManage),
        RetiredCode(PermissionCatalog.Retired.WorkspacesRead),
        RetiredCode(PermissionCatalog.Retired.WorkspacesCreate),
        RetiredCode(PermissionCatalog.Retired.WorkspacesManage),
        RetiredCode(PermissionCatalog.Retired.ResourcesRead),
        RetiredCode(PermissionCatalog.Retired.ResourcesManage),
        RetiredCode(PermissionCatalog.Retired.RoadmapsRead),
        RetiredCode(PermissionCatalog.Retired.RoadmapsWrite),
        // Never enforced by any endpoint or screen. settings:billing duplicated billing:manage,
        // settings:branding is covered by legal_entity:update, settings:read by legal_entity:read.
        RetiredCode(PermissionCatalog.Retired.SettingsRead),
        RetiredCode(PermissionCatalog.Retired.SettingsAdmin),
        RetiredCode(PermissionCatalog.Retired.SettingsBilling),
        RetiredCode(PermissionCatalog.Retired.SettingsBranding),
        RetiredCode(PermissionCatalog.Retired.SettingsIntegrations),
        RetiredCode(PermissionCatalog.Retired.SettingsAlerts),
        RetiredCode(PermissionCatalog.Retired.SettingsSystem),
        // Employee visibility is relationship-based (management coverage), not a permission:
        // a manager sees the people their position covers without any code.
        RetiredCode(PermissionCatalog.Retired.EmployeesRead),
        RetiredCode(PermissionCatalog.Retired.EmployeesReadTeam),
        RetiredCode(PermissionCatalog.Retired.AttendanceReadTeam),
        // Invitation resend/revoke is allowed for the person who added the employee, or for
        // users:manage (Settings > Users) - see EmployeeInvitationAccess.
        RetiredCode(PermissionCatalog.Retired.InvitationsManage),
    ];

    private static readonly Dictionary<string, PermissionDefinition> ByCode =
        Definitions.ToDictionary(d => d.Code, StringComparer.Ordinal);

    /// <summary>Every known code, including auto-granted, derived, not-seeded and retired ones.</summary>
    public static IReadOnlyList<PermissionDefinition> All => Definitions;

    /// <summary>The codes <c>PermissionSeeder</c> writes to the <c>permissions</c> table.</summary>
    public static IReadOnlyList<PermissionDefinition> Seeded { get; } =
        Definitions.Where(d => d.Kind == PermissionKind.Assignable).ToArray();

    /// <summary>Soft-retired and removed codes. No user can hold them; do not check them.</summary>
    public static IReadOnlySet<string> RetiredCodes { get; } =
        Definitions.Where(d => d.IsRetired).Select(d => d.Code).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Old code to new code. The sync updates the <c>permissions</c> row in place, so every role grant and user
    /// override follows it and nothing cascades. Take the old constant out of the catalogue in the same change;
    /// keep the entry for as long as any database may still hold the old code. Each entry also gets a line in
    /// docs/api/permission-changelog.md.
    /// </summary>
    public static IReadOnlyList<PermissionRename> Renames { get; } =
    [
    ];

    /// <summary>
    /// One-time backfill of a newly added code to matching roles of existing tenants (for example
    /// <c>DefaultGrantTarget.Owner</c>). It runs once per database, so an admin who later removes the code
    /// from a role is not overruled. Declare it only together with a new code.
    /// </summary>
    public static IReadOnlyList<PermissionDefaultGrant> DefaultGrants { get; } =
    [
    ];

    /// <summary>Everything PermissionCatalogSynchronizer applies besides writing <see cref="Seeded"/>.</summary>
    public static PermissionLifecycleRules Lifecycle { get; } = new(
        Renames,
        DefaultGrants,
        Definitions
            .Where(d => d.IsRetired)
            .Select(d => new PermissionRetirement(
                d.Code,
                d.Kind == PermissionKind.SoftRetired ? RetirementStage.SoftRetired : RetirementStage.Removed,
                d.Description,
                d.DropGrants))
            .ToArray());

    /// <summary>The definition for a code, or null when the code is not in the catalogue.</summary>
    public static PermissionDefinition? Find(string code) => ByCode.GetValueOrDefault(code);

    private static PermissionDefinition Assignable(string code, string module, string description) =>
        new(code, module, description, PermissionKind.Assignable);

    private static PermissionDefinition Definition(string code, string module, string description, PermissionKind kind) =>
        new(code, module, description, kind);

    /// <summary>Retired before task 33, when the seeder deleted these rows and their grants at every start.
    /// DropGrants keeps old databases converging the same way.</summary>
    private static PermissionDefinition RetiredCode(string code) =>
        new(code, string.Empty, "Retired before task 33.", PermissionKind.Retired, DropGrants: true);
}
