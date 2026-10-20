using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Registry;
using ONEVO.Application.Features.Dashboard.Layout.Services;

namespace ONEVO.Application.Features.Dashboard.Layout.Queries.GetMyDashboards;

public sealed class GetMyDashboardsQueryHandler(DashboardSeeder seeder, ICurrentUser currentUser)
    : IRequestHandler<GetMyDashboardsQuery, Result<IReadOnlyList<DashboardSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<DashboardSummaryDto>>> Handle(GetMyDashboardsQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<IReadOnlyList<DashboardSummaryDto>>.Forbidden();

        var work = await seeder.EnsureAsync(DashboardKeys.MyWork, ct);
        var team = await seeder.EnsureAsync(DashboardKeys.MyTeam, ct);

        IReadOnlyList<DashboardSummaryDto> list =
        [
            new(work.Id, work.DashboardKey, "My Work", work.LayoutVersion),
            new(team.Id, team.DashboardKey, "My Team", team.LayoutVersion),
        ];
        return Result<IReadOnlyList<DashboardSummaryDto>>.Success(list);
    }
}
