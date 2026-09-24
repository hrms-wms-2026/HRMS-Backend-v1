using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Offboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Queries.ListMyPendingBypassRequests;

public class ListMyPendingBypassRequestsQueryHandler(IOffboardingTaskBypassRequestRepository repository, ICurrentUser currentUser)
    : IRequestHandler<ListMyPendingBypassRequestsQuery, Result<IReadOnlyList<BypassRequestResponse>>>
{
    public async Task<Result<IReadOnlyList<BypassRequestResponse>>> Handle(ListMyPendingBypassRequestsQuery request, CancellationToken ct)
    {
        var requests = await repository.ListPendingByApproverAsync(currentUser.TenantId, currentUser.UserId, ct);
        return Result<IReadOnlyList<BypassRequestResponse>>.Success(requests.Select(r => new BypassRequestResponse(
            r.Id, r.EmployeeChecklistTaskId, r.OffboardingRecordId, r.RequestedById,
            r.BypassReason, r.PenaltyDescription, r.Status, r.RequestedAt)).ToList());
    }
}
