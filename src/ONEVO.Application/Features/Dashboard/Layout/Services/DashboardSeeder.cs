using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Application.Features.Dashboard.Layout.Services;

/// <summary>Lazily creates a user's dashboard from the FULL default layout (regardless of permissions).</summary>
public sealed class DashboardSeeder(IUserDashboardRepository dashboards, ICurrentUser currentUser)
{
    public async Task<UserDashboard> EnsureAsync(string dashboardKey, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var userId = currentUser.UserId;

        var existing = await dashboards.GetAsync(tenantId, userId, dashboardKey, ct);
        if (existing is not null) return existing;

        var dashboard = new UserDashboard
        {
            TenantId = tenantId,
            UserId = userId,
            DashboardKey = dashboardKey,
            Widgets = BuildDefaultWidgets(tenantId, dashboardKey),
        };

        if (await dashboards.TryAddAsync(dashboard, ct)) return dashboard;

        // Lost the seeding race: the winner's row exists.
        return await dashboards.GetAsync(tenantId, userId, dashboardKey, ct)
            ?? throw new InvalidOperationException($"Dashboard '{dashboardKey}' was not found after a seeding race.");
    }

    /// <summary>Widgets (with desktop/tablet/mobile layouts) for the full default layout. Reused by Reset.</summary>
    public static List<DashboardWidget> BuildDefaultWidgets(Guid tenantId, string dashboardKey)
    {
        var byBreakpoint = Breakpoints.All.ToDictionary(
            b => b, b => DashboardWidgetRegistry.DefaultLayout(dashboardKey, b).ToDictionary(i => i.Key, i => i.Rect));

        return DashboardWidgetRegistry.DefaultLayout(dashboardKey, Breakpoints.Desktop)
            .Select(item => new DashboardWidget
            {
                TenantId = tenantId,
                WidgetKey = item.Key,
                SettingsJson = "{}",
                Layouts = Breakpoints.All.Select(b =>
                {
                    var r = byBreakpoint[b][item.Key];
                    return new DashboardWidgetLayout { TenantId = tenantId, Breakpoint = b, X = r.X, Y = r.Y, W = r.W, H = r.H };
                }).ToList(),
            }).ToList();
    }
}
