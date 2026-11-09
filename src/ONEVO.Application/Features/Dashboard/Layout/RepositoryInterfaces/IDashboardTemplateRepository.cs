using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;

/// <summary>Read-only access to the tenant's dashboard templates. Templates are written by the Administrative
/// application and the startup seeder, never by the dashboard runtime.</summary>
public interface IDashboardTemplateRepository
{
    /// <summary>Active, non-deleted templates (widgets + layouts, untracked) for these roles and dashboard key.
    /// Soft-deleted child widgets and layouts are excluded.</summary>
    Task<IReadOnlyList<DashboardTemplate>> ListForRolesAsync(
        Guid tenantId, string dashboardKey, IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default);

    /// <summary>The tenant's active No-role template (is_no_role_template = true) for the key, or null.</summary>
    Task<DashboardTemplate?> GetNoRoleAsync(Guid tenantId, string dashboardKey, CancellationToken ct = default);
}
