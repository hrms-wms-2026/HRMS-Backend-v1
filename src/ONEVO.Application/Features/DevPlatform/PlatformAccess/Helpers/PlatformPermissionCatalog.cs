namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

/// <summary>
/// One seeded Developer Platform permission. Codes come verbatim from
/// developer-platform docs; see PLATFORM_PERMISSION_CATALOG.md for provenance.
/// </summary>
public sealed record PlatformPermissionDefinition(
    string Code,
    string ModuleKey,
    string Description,
    bool IsHighRisk);

/// <summary>
/// Canonical Phase 1 Developer Platform permission catalog, generated from
/// access-control-contract.md, backend/api-contracts.md, and Phase 1 module docs.
/// Do not add codes here that are not documented (PLATFORM_PERMISSION_GAPS.md
/// tracks excluded/ambiguous codes).
/// </summary>
public static class PlatformPermissionCatalog
{
    public const string PermissionClaimType = "platform_permission";

    public const string DashboardView = "platform.dashboard.view";
    public const string TenantsRead = "platform.tenants.read";
    public const string TenantsManage = "platform.tenants.manage";
    public const string TenantsImpersonate = "platform.tenants.impersonate";
    public const string TenantsFeatureOverridesRead = "platform.tenants.feature_overrides.read";
    public const string TenantsFeatureOverridesManage = "platform.tenants.feature_overrides.manage";
    public const string SubscriptionsRead = "platform.subscriptions.read";
    public const string SubscriptionsManage = "platform.subscriptions.manage";
    public const string ModuleCatalogRead = "platform.module_catalog.read";
    public const string ModuleCatalogManage = "platform.module_catalog.manage";
    public const string DemoProfilesRead = "platform.demo_profiles.read";
    public const string DemoProfilesManage = "platform.demo_profiles.manage";
    public const string RequestsRead = "platform.requests.read";
    public const string RequestsManage = "platform.requests.manage";
    public const string SupportRead = "platform.support.read";
    public const string SupportManage = "platform.support.manage";
    public const string AccountsRead = "platform.accounts.read";
    public const string AccountsManage = "platform.accounts.manage";
    public const string RolesRead = "platform.roles.read";
    public const string RolesManage = "platform.roles.manage";
    public const string TemplatesRead = "platform.templates.read";
    public const string TemplatesManage = "platform.templates.manage";
    public const string RuntimeFlagsRead = "platform.runtime_flags.read";
    public const string RuntimeFlagsManage = "platform.runtime_flags.manage";
    public const string SecurityRead = "platform.security.read";
    public const string SecurityManage = "platform.security.manage";
    public const string AuditRead = "platform.audit.read";
    public const string AuditExport = "platform.audit.export";
    public const string ComplianceRead = "platform.compliance.read";
    public const string ComplianceManage = "platform.compliance.manage";
    public const string ReportsRead = "platform.reports.read";
    public const string SystemConfigRead = "platform.system_config.read";
    public const string SystemConfigManage = "platform.system_config.manage";
    public const string OperationsRead = "platform.operations.read";
    public const string OperationsManage = "platform.operations.manage";
    public const string AppCatalogRead = "platform.app_catalog.read";
    public const string AppCatalogManage = "platform.app_catalog.manage";

    public static IReadOnlyList<PlatformPermissionDefinition> GetAll() => new List<PlatformPermissionDefinition>
    {
        new(DashboardView, "dashboard", "View the Developer Platform dashboard", false),
        new(TenantsRead, "tenant-management", "View tenants and tenant detail tabs", false),
        new(TenantsManage, "tenant-management", "Create, edit, provision, suspend, and reactivate tenants", false),
        new(TenantsImpersonate, "tenant-management", "Issue short-lived tenant impersonation tokens", true),
        new(TenantsFeatureOverridesRead, "tenant-management", "View tenant feature flag and module runtime overrides", false),
        new(TenantsFeatureOverridesManage, "tenant-management", "Change tenant feature flag and module runtime overrides", false),
        new(SubscriptionsRead, "subscription-plans", "View subscription plans, invoices, and billing data", false),
        new(SubscriptionsManage, "subscription-plans", "Manage subscription plans, invoices, and commercial terms", false),
        new(ModuleCatalogRead, "module-catalog", "View the product module and integration catalogs", false),
        new(ModuleCatalogManage, "module-catalog", "Manage the product module and integration catalogs", false),
        new(DemoProfilesRead, "demo-profiles", "View demo profiles", false),
        new(DemoProfilesManage, "demo-profiles", "Manage demo profiles and upgrade options", false),
        new(RequestsRead, "requests-center", "View demo access and trial extension requests", false),
        new(RequestsManage, "requests-center", "Approve or reject demo access and trial extension requests", false),
        new(SupportRead, "customer-support", "View support tickets", false),
        new(SupportManage, "customer-support", "Assign, reply to, and close support tickets", false),
        new(AccountsRead, "platform-users", "View platform users, sessions, and access history", false),
        new(AccountsManage, "platform-users", "Invite, deactivate, and manage platform users and their sessions", true),
        new(RolesRead, "platform-roles", "View platform roles and the permission catalog", false),
        new(RolesManage, "platform-roles", "Create and edit platform roles and their permissions", true),
        new(TemplatesRead, "template-management", "View role and configuration templates", false),
        new(TemplatesManage, "template-management", "Manage role and configuration templates", false),
        new(RuntimeFlagsRead, "system-config", "View global feature flag definitions", false),
        new(RuntimeFlagsManage, "system-config", "Manage global feature flag definitions", false),
        new(SecurityRead, "security-center", "View security overview, alerts, and platform sessions", false),
        new(SecurityManage, "security-center", "Acknowledge/resolve alerts and revoke platform sessions", true),
        new(AuditRead, "audit-console", "Query the cross-tenant audit log", false),
        new(AuditExport, "audit-console", "Export audit log data", false),
        new(ComplianceRead, "compliance-center", "View compliance status, legal holds, and legal documents", false),
        new(ComplianceManage, "compliance-center", "Manage legal holds, retention policies, and legal document versions", true),
        new(ReportsRead, "reports-analytics", "View platform reports and analytics", false),
        new(SystemConfigRead, "system-config", "View global system configuration (secrets never returned)", false),
        new(SystemConfigManage, "system-config", "Manage global system configuration, provider credentials, and service keys", true),
        new(OperationsRead, "operations", "View operations/platform health surfaces", false),
        new(OperationsManage, "operations", "Execute approved operations service actions", false),
        new(AppCatalogRead, "app-catalog", "View the global app catalog", false),
        new(AppCatalogManage, "app-catalog", "Manage the global app catalog", false),
    };
}
