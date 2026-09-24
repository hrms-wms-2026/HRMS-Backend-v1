using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Mappers;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Queries.ListMyObjectiveChangeRequests;

public class ListMyObjectiveChangeRequestsQueryHandler : IRequestHandler<ListMyObjectiveChangeRequestsQuery, Result<IReadOnlyList<ObjectiveChangeRequestResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveChangeRequestRepository _changeRequests;

    public ListMyObjectiveChangeRequestsQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveChangeRequestRepository changeRequests)
    {
        _currentUser = currentUser;
        _identity = identity;
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

        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<ObjectiveChangeRequestResponse>>.Forbidden("No employee record for the current user.");

        var pending = await _changeRequests.ListPendingForApproverAsync(tenantId, callerEmployeeId.Value, ct);
        var items = pending.Select(ObjectiveMapper.ToResponse).ToList();

        return Result<IReadOnlyList<ObjectiveChangeRequestResponse>>.Success(items);
    }
}
