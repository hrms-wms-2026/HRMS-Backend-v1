using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;

public interface IUserDashboardRepository
{
    /// <summary>The user's dashboard with widgets and layouts (tracked), or null when not seeded yet.</summary>
    Task<UserDashboard?> GetAsync(Guid tenantId, Guid userId, string dashboardKey, CancellationToken ct = default);

    /// <summary>A dashboard by id with widgets and layouts (tracked); ownership is the caller's check.</summary>
    Task<UserDashboard?> GetByIdAsync(Guid tenantId, Guid dashboardId, CancellationToken ct = default);

    /// <summary>
    /// The dashboard that holds the given widget instance, with widgets and layouts (tracked), or null.
    /// Ownership is the caller's check.
    /// </summary>
    Task<UserDashboard?> GetByWidgetIdAsync(Guid tenantId, Guid widgetId, CancellationToken ct = default);

    /// <summary>The user's dashboards without widgets or layouts.</summary>
    Task<IReadOnlyList<UserDashboard>> ListForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Inserts a freshly seeded dashboard; false when another request already seeded it (unique violation).</summary>
    Task<bool> TryAddAsync(UserDashboard dashboard, CancellationToken ct = default);

    /// <summary>
    /// Saves a tracked dashboard (including added/removed widgets and layouts) and bumps LayoutVersion to
    /// expectedVersion + 1, only if the stored version still equals expectedVersion; false on a version mismatch.
    /// </summary>
    Task<bool> SaveWithVersionAsync(UserDashboard dashboard, long expectedVersion, CancellationToken ct = default);
}
