using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Access;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Services;

namespace ONEVO.Application.Features.Dashboard.Layout.Commands.RemoveDashboardWidget;

public sealed class RemoveDashboardWidgetCommandHandler(
    IUserDashboardRepository dashboards,
    IDashboardWidgetAccess access,
    ICurrentUser currentUser)
    : IRequestHandler<RemoveDashboardWidgetCommand, Result<long>>
{
    public async Task<Result<long>> Handle(RemoveDashboardWidgetCommand command, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<long>.Forbidden();

        var dashboard = await dashboards.GetByIdAsync(currentUser.TenantId, command.DashboardId, ct);
        if (dashboard is null || dashboard.UserId != currentUser.UserId)
            return Result<long>.NotFound("Dashboard not found.");

        if (dashboard.LayoutVersion != command.ExpectedVersion)
            return DashboardLayoutSupport.Conflict<long>(dashboard.LayoutVersion);

        var allowed = await access.GetAllowedKeysAsync(dashboard.DashboardKey, ct);
        var visible = DashboardLayoutSupport.VisibleWidgets(dashboard, allowed);
        var widget = visible.FirstOrDefault(w => w.Id == command.InstanceId);
        if (widget is null)
            return Result<long>.NotFound("Widget not found.");

        visible.Remove(widget);
        DashboardLayoutSupport.RemoveWidget(dashboard, widget);
        DashboardLayoutSupport.CompactVisible(visible);

        var expected = command.ExpectedVersion;
        if (!await dashboards.SaveWithVersionAsync(dashboard, expected, ct))
            return await DashboardLayoutSupport.ConflictAfterRaceAsync<long>(
                dashboards, dashboard.TenantId, dashboard.Id, expected, ct);

        return Result<long>.Success(dashboard.LayoutVersion);
    }
}
