using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Dashboard.Layout.Queries.GetMyDashboards;

public sealed record DashboardSummaryDto(Guid Id, string Key, string Title, long LayoutVersion);

public sealed record GetMyDashboardsQuery : IRequest<Result<IReadOnlyList<DashboardSummaryDto>>>;
