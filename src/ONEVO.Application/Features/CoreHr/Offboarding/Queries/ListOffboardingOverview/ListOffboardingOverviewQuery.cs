using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListOffboardingOverview;

public sealed record ListOffboardingOverviewQuery(int Page = 1, int PageSize = 25)
    : IRequest<Result<IReadOnlyList<OffboardingOverviewItemResponse>>>;
