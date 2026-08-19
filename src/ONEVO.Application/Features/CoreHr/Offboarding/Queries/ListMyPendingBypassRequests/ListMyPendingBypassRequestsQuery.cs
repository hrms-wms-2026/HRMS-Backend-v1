using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListMyPendingBypassRequests;

public sealed record ListMyPendingBypassRequestsQuery : IRequest<Result<IReadOnlyList<BypassRequestResponse>>>;
