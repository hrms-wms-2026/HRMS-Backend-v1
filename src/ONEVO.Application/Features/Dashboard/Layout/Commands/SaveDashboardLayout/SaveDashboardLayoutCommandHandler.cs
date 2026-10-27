using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Access;
using ONEVO.Application.Features.Dashboard.Layout.Grid;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Services;

namespace ONEVO.Application.Features.Dashboard.Layout.Commands.SaveDashboardLayout;

public sealed class SaveDashboardLayoutCommandHandler(
    IUserDashboardRepository dashboards,
    IDashboardWidgetAccess access,
    ICurrentUser currentUser)
    : IRequestHandler<SaveDashboardLayoutCommand, Result<long>>
{
    public async Task<Result<long>> Handle(SaveDashboardLayoutCommand command, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<long>.Forbidden();

        if (!Breakpoints.All.Contains(command.Breakpoint))
            return Result<long>.UnprocessableEntity($"Unknown breakpoint '{command.Breakpoint}'.");

        var dashboard = await dashboards.GetByIdAsync(currentUser.TenantId, command.DashboardId, ct);
        if (dashboard is null || dashboard.UserId != currentUser.UserId)
            return Result<long>.NotFound("Dashboard not found.");

        if (dashboard.LayoutVersion != command.ExpectedVersion)
            return DashboardLayoutSupport.Conflict<long>(dashboard.LayoutVersion);

        var allowed = await access.GetAllowedKeysAsync(dashboard.DashboardKey, ct);
        var visible = DashboardLayoutSupport.VisibleWidgets(dashboard, allowed);

        var submitted = command.Widgets ?? [];
        var submittedIds = submitted.Select(i => i.InstanceId).ToList();
        if (submittedIds.Count != visible.Count
            || submittedIds.Distinct().Count() != submittedIds.Count
            || !visible.Select(w => w.Id).ToHashSet().SetEquals(submittedIds))
            return Result<long>.UnprocessableEntity(DashboardLayoutSupport.LayoutSetMismatch);

        var byId = visible.ToDictionary(w => w.Id);
        var columns = Breakpoints.Columns(command.Breakpoint);
        var errors = new List<string>();

        // Known widgets go through the shared grid rules; retired keys only need bounds and no overlap.
        var known = submitted
            .Where(i => DashboardWidgetRegistry.Get(byId[i.InstanceId].WidgetKey) is not null)
            .Select(i => (byId[i.InstanceId].WidgetKey, new GridRect(i.X, i.Y, i.W, i.H)))
            .ToList();
        errors.AddRange(DashboardGrid.Validate(known, command.Breakpoint));

        var retired = submitted.Where(i => DashboardWidgetRegistry.Get(byId[i.InstanceId].WidgetKey) is null).ToList();
        foreach (var i in retired)
        {
            if (i.X < 0 || i.Y < 0 || i.W < 1 || i.H < 1 || i.X >= columns || i.W > columns - i.X || i.H > DashboardGrid.MaxRows - i.Y)
                errors.Add(DashboardGrid.OutOfBounds);
        }

        if (retired.Count > 0 && !errors.Contains(DashboardGrid.Overlap))
        {
            var overlaps = retired.Any(r =>
                submitted.Any(o => o.InstanceId != r.InstanceId
                    && DashboardGrid.Overlaps(new GridRect(r.X, r.Y, r.W, r.H), new GridRect(o.X, o.Y, o.W, o.H))));
            if (overlaps)
                errors.Add(DashboardGrid.Overlap);
        }

        if (errors.Count > 0)
            return Result<long>.UnprocessableEntity(string.Join(",", errors.Distinct()));

        foreach (var item in submitted)
            DashboardLayoutSupport.SetRect(byId[item.InstanceId], command.Breakpoint,
                new GridRect(item.X, item.Y, item.W, item.H), dashboard.TenantId);

        var expected = command.ExpectedVersion;
        if (!await dashboards.SaveWithVersionAsync(dashboard, expected, ct))
            return await DashboardLayoutSupport.ConflictAfterRaceAsync<long>(
                dashboards, dashboard.TenantId, dashboard.Id, expected, ct);

        return Result<long>.Success(dashboard.LayoutVersion);
    }
}
