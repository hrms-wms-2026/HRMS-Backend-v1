using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskCreationRequests;

public class GetMyTaskCreationRequestsQueryHandler : IRequestHandler<GetMyTaskCreationRequestsQuery, Result<IReadOnlyList<TaskCreationRequestResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskCreationRequestRepository _requests;

    public GetMyTaskCreationRequestsQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ITaskCreationRequestRepository requests)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
    }

    public async Task<Result<IReadOnlyList<TaskCreationRequestResponse>>> Handle(GetMyTaskCreationRequestsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskCreationRequestResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<TaskCreationRequestResponse>>.Forbidden("No employee record for the current user.");

        var pending = await _requests.GetPendingForOwnerEmployeeIdAsync(tenantId, callerEmployeeId.Value, ct);
        var items = pending.Select(r =>
        {
            var payload = JsonSerializer.Deserialize<TaskCreationRequestPayload>(r.PayloadJson)!;
            return new TaskCreationRequestResponse(r.Id, r.ObjectiveId, r.Status, payload, r.CreatedAt);
        }).ToList();

        return Result<IReadOnlyList<TaskCreationRequestResponse>>.Success(items);
    }
}
