using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Access;
using ONEVO.Application.Features.Dashboard.Layout.DTOs;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;

namespace ONEVO.Application.Features.Dashboard.Layout.Queries.GetAvailableWidgets;

public sealed class GetAvailableWidgetsQueryHandler(
    IUserDashboardRepository dashboards,
    IDashboardWidgetAccess access,
    ICurrentUser currentUser)
    : IRequestHandler<GetAvailableWidgetsQuery, Result<IReadOnlyList<AvailableWidgetDto>>>
{
    public async Task<Result<IReadOnlyList<AvailableWidgetDto>>> Handle(GetAvailableWidgetsQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<IReadOnlyList<AvailableWidgetDto>>.Forbidden();

        if (!Breakpoints.All.Contains(query.Breakpoint))
            return Result<IReadOnlyList<AvailableWidgetDto>>.UnprocessableEntity($"Unknown breakpoint '{query.Breakpoint}'.");

        var dashboard = await dashboards.GetByIdAsync(currentUser.TenantId, query.DashboardId, ct);
        if (dashboard is null || dashboard.UserId != currentUser.UserId)
            return Result<IReadOnlyList<AvailableWidgetDto>>.NotFound("Dashboard not found.");

        var columns = Breakpoints.Columns(query.Breakpoint);
        var allowed = await access.GetAllowedKeysAsync(dashboard.DashboardKey, ct);
        var placed = dashboard.Widgets.Select(w => w.WidgetKey).ToHashSet(StringComparer.Ordinal);

        var items = DashboardWidgetRegistry.ForDashboard(dashboard.DashboardKey)
            .Where(d => allowed.Contains(d.Key))
            .Select(d =>
            {
                var (dw, dh) = DashboardWidgetRegistry.DefaultSize(d.Key);
                return new AvailableWidgetDto(
                    d.Key, d.Title, d.Description,
                    new WidgetSizeDto(Math.Min(d.MinW, columns), d.MinH, Math.Min(d.MaxW, columns), d.MaxH),
                    Math.Min(dw, columns), dh, placed.Contains(d.Key));
            })
            .ToList();

        return Result<IReadOnlyList<AvailableWidgetDto>>.Success(items);
    }
}
