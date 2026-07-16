namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;

/// <summary>
/// Seeded system role names from access-control-contract.md ("Seeded System Roles").
/// Only Platform Super Admin has a documented exact permission mapping ("All platform
/// permissions"); the other roles' doc scopes are prose, so they are seeded with empty
/// permission sets — see PLATFORM_ROLE_PERMISSION_GAPS.md.
/// </summary>
public static class PlatformRoleCatalog
{
    public const string SuperAdmin = "Platform Super Admin";
    public const string TenantOperationsManager = "Tenant Operations Manager";
    public const string BillingManager = "Billing Manager";
    public const string SecurityAuditor = "Security Auditor";
    public const string ModuleCatalogManager = "Module Catalog Manager";
    public const string OperationsEngineer = "Operations Engineer";
    public const string ReadOnlyViewer = "Read-Only Viewer";

    public sealed record SystemRoleDefinition(string Name, string Description);

    public static IReadOnlyList<SystemRoleDefinition> GetSystemRoles() => new List<SystemRoleDefinition>
    {
        new(SuperAdmin, "All platform permissions. Cannot be deleted."),
        new(TenantOperationsManager, "Tenant lifecycle, provisioning, activation, and tenant read/manage actions."),
        new(BillingManager, "Subscription plans, invoices, payment gateways, and billing reports."),
        new(SecurityAuditor, "Security, audit, compliance, and retention read-only access."),
        new(ModuleCatalogManager, "Product module catalog, integration catalog, and template management."),
        new(OperationsEngineer, "Platform health, services monitor, tenant runtime overrides, and system configuration review."),
        new(ReadOnlyViewer, "Dashboard and broad read-only visibility without mutation permissions."),
    };
}
