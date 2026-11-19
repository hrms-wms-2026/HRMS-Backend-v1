using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Services;

namespace ONEVO.Application.Features.Dashboard.Layout.Commands.ResetDashboard;

/// <summary>
/// Restores the FULL default layout. Existing rows for default widgets are reused (never removed and
/// re-created with the same key in one save, which could trip the partial unique indexes).
/// </summary>
public sealed class ResetDashboardCommandHandler(
    IUserDashboardRepository dashboards,
    ICurrentUser currentUser)
    : IRequestHandler<ResetDashboardCommand, Result<long>>
{
    public async Task<Result<long>> Handle(ResetDashboardCommand command, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<long>.Forbidden();

        var dashboard = await dashboards.GetByIdAsync(currentUser.TenantId, command.DashboardId, ct);
        if (dashboard is null || dashboard.UserId != currentUser.UserId)
            return Result<long>.NotFound("Dashboard not found.");

        if (dashboard.LayoutVersion != command.ExpectedVersion)
            return DashboardLayoutSupport.Conflict<long>(dashboard.LayoutVersion);

        var tenantId = dashboard.TenantId;
        var defaults = DashboardSeeder.BuildDefaultWidgets(tenantId, dashboard.DashboardKey);
        var defaultKeys = defaults.Select(d => d.WidgetKey).ToHashSet();

        foreach (var stale in dashboard.Widgets.Where(w => !defaultKeys.Contains(w.WidgetKey)).ToList())
            DashboardLayoutSupport.RemoveWidget(dashboard, stale);

        foreach (var def in defaults)
        {
            var existing = dashboard.Widgets.FirstOrDefault(w => w.WidgetKey == def.WidgetKey);
            if (existing is null)
            {
                def.Id = Guid.NewGuid();
                def.DashboardId = dashboard.Id;
                foreach (var l in def.Layouts)
                {
                    l.Id = Guid.NewGuid();
                    l.WidgetId = def.Id;
                }

                dashboard.Widgets.Add(def);
                continue;
            }

            existing.SettingsJson = "{}";
            foreach (var l in def.Layouts)
                DashboardLayoutSupport.SetRect(existing, l.Breakpoint, new GridRect(l.X, l.Y, l.W, l.H), tenantId);
        }

        var expected = command.ExpectedVersion;
        if (!await dashboards.SaveWithVersionAsync(dashboard, expected, ct))
            return await DashboardLayoutSupport.ConflictAfterRaceAsync<long>(
                dashboards, tenantId, dashboard.Id, expected, ct);

        return Result<long>.Success(dashboard.LayoutVersion);
    }
}
