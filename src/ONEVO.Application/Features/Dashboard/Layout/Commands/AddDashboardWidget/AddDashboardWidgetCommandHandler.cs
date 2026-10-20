using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Access;
using ONEVO.Application.Features.Dashboard.Layout.Grid;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Services;
using ONEVO.Domain.Features.Dashboard.Entities;

namespace ONEVO.Application.Features.Dashboard.Layout.Commands.AddDashboardWidget;

public sealed class AddDashboardWidgetCommandHandler(
    IUserDashboardRepository dashboards,
    IDashboardWidgetAccess access,
    ICurrentUser currentUser)
    : IRequestHandler<AddDashboardWidgetCommand, Result<AddWidgetResultDto>>
{
    public async Task<Result<AddWidgetResultDto>> Handle(AddDashboardWidgetCommand command, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<AddWidgetResultDto>.Forbidden();

        var dashboard = await dashboards.GetByIdAsync(currentUser.TenantId, command.DashboardId, ct);
        if (dashboard is null || dashboard.UserId != currentUser.UserId)
            return Result<AddWidgetResultDto>.NotFound("Dashboard not found.");

        if (dashboard.LayoutVersion != command.ExpectedVersion)
            return DashboardLayoutSupport.Conflict<AddWidgetResultDto>(dashboard.LayoutVersion);

        var def = DashboardWidgetRegistry.Get(command.WidgetKey);
        var allowed = await access.GetAllowedKeysAsync(dashboard.DashboardKey, ct);
        if (def is null || def.DashboardKey != dashboard.DashboardKey || !allowed.Contains(def.Key))
            return Result<AddWidgetResultDto>.Forbidden("Widget is not available on this dashboard.");

        if (dashboard.Widgets.Any(w => w.WidgetKey == def.Key))
            return Result<AddWidgetResultDto>.Conflict(DashboardLayoutSupport.WidgetAlreadyPlaced);

        var visible = DashboardLayoutSupport.VisibleWidgets(dashboard, allowed);
        var tenantId = dashboard.TenantId;

        // A caller-supplied desktop rect is honoured only when valid and free of visible widgets;
        // otherwise the widget is auto-placed at its default size (Ruling 5).
        var desktopOccupied = visible.Select(w => DashboardLayoutSupport.Rect(w, Breakpoints.Desktop)).OfType<GridRect>().ToList();
        var requested = command.DesktopRect is { } r
            && DashboardGrid.Validate([(def.Key, r)], Breakpoints.Desktop).Count == 0
            && !desktopOccupied.Any(o => DashboardGrid.Overlaps(o, r))
            ? r : null;
        var (defaultW, defaultH) = DashboardWidgetRegistry.DefaultSize(def.Key);
        var desktopW = requested?.W ?? defaultW;
        var desktopH = requested?.H ?? defaultH;

        var widget = new DashboardWidget
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DashboardId = dashboard.Id,
            WidgetKey = def.Key, SettingsJson = "{}",
        };

        foreach (var breakpoint in Breakpoints.All)
        {
            var columns = Breakpoints.Columns(breakpoint);
            var occupied = visible
                .Select(w => DashboardLayoutSupport.Rect(w, breakpoint))
                .OfType<GridRect>()
                .ToList();

            GridRect rect;
            if (breakpoint == Breakpoints.Desktop && requested is not null)
            {
                rect = requested;
            }
            else
            {
                var w = breakpoint switch
                {
                    Breakpoints.Mobile => columns,
                    Breakpoints.Tablet => Math.Max(desktopW >= 6 ? 6 : 3, Math.Min(def.MinW, columns)),
                    _ => desktopW,
                };
                rect = DashboardGrid.AutoPlace(occupied, Math.Min(w, columns), desktopH, columns);
            }

            DashboardLayoutSupport.SetRect(widget, breakpoint, rect, tenantId);
        }

        dashboard.Widgets.Add(widget);
        DashboardLayoutSupport.CompactVisible([.. visible, widget]);

        var expected = command.ExpectedVersion;
        if (!await dashboards.SaveWithVersionAsync(dashboard, expected, ct))
            return await DashboardLayoutSupport.ConflictAfterRaceAsync<AddWidgetResultDto>(
                dashboards, tenantId, dashboard.Id, expected, ct);

        return Result<AddWidgetResultDto>.Success(new AddWidgetResultDto(widget.Id, dashboard.LayoutVersion));
    }
}
