using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Services;

namespace ONEVO.Application.Features.Dashboard.Layout.Queries.GetMyDashboards;

public sealed class GetMyDashboardsQueryHandler(
    DashboardSeeder seeder, IUserDashboardRepository dashboards, ICurrentUser currentUser)
    : IRequestHandler<GetMyDashboardsQuery, Result<IReadOnlyList<DashboardSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<DashboardSummaryDto>>> Handle(GetMyDashboardsQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<IReadOnlyList<DashboardSummaryDto>>.Forbidden();

        // Header rows only; the race-safe seeder (full graph) runs just for dashboards that do not exist yet.
        var existing = await dashboards.ListForUserAsync(currentUser.TenantId, currentUser.UserId, ct);

        var list = new List<DashboardSummaryDto>();
        foreach (var key in (string[])[DashboardKeys.MyWork, DashboardKeys.MyTeam])
        {
            var dashboard = existing.FirstOrDefault(d => d.DashboardKey == key)
                ?? await seeder.EnsureAsync(key, ct);
            list.Add(new DashboardSummaryDto(dashboard.Id, dashboard.DashboardKey, DashboardKeys.Title(key), dashboard.LayoutVersion));
        }

        return Result<IReadOnlyList<DashboardSummaryDto>>.Success(list);
    }
}
