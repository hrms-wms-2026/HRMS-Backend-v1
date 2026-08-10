using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;

public class ListMyObjectiveChangeRequestsQueryHandler : IRequestHandler<ListMyObjectiveChangeRequestsQuery, Result<IReadOnlyList<ObjectiveChangeRequestResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IObjectiveChangeRequestRepository _changeRequests;

    public ListMyObjectiveChangeRequestsQueryHandler(ICurrentUser currentUser, IObjectiveChangeRequestRepository changeRequests)
    {
        _currentUser = currentUser;
        _changeRequests = changeRequests;
    }

    public async Task<Result<IReadOnlyList<ObjectiveChangeRequestResponse>>> Handle(ListMyObjectiveChangeRequestsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ObjectiveChangeRequestResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        if (tenantId == Guid.Empty)
            return Result<IReadOnlyList<ObjectiveChangeRequestResponse>>.Forbidden("Tenant context missing.");

        var pending = await _changeRequests.ListPendingForApproverAsync(tenantId, userId, ct);
        var items = pending.Select(ObjectiveMapper.ToResponse).ToList();

        return Result<IReadOnlyList<ObjectiveChangeRequestResponse>>.Success(items);
    }
}
