using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskEditRequests;

public class GetMyTaskEditRequestsQueryHandler
    : IRequestHandler<GetMyTaskEditRequestsQuery, Result<IReadOnlyList<TaskEditRequestResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ITaskEditRequestRepository _requests;

    public GetMyTaskEditRequestsQueryHandler(
        ICurrentUser currentUser,
        ICallerIdentityResolver identity,
        ITaskEditRequestRepository requests)
    {
        _currentUser = currentUser;
        _identity = identity;
        _requests = requests;
    }

    public async Task<Result<IReadOnlyList<TaskEditRequestResponse>>> Handle(
        GetMyTaskEditRequestsQuery request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<TaskEditRequestResponse>>.Forbidden(
                "Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(
            tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<TaskEditRequestResponse>>.Forbidden(
                "No employee record for the current user.");

        var pending = await _requests.GetPendingForOwnerEmployeeIdAsync(
            tenantId, callerEmployeeId.Value, ct);
        var requesterIds = pending
            .Select(r => r.RequestedByEmployeeId)
            .Distinct()
            .ToList();
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, requesterIds, ct);

        var items = pending.Select(r =>
        {
            var payload = JsonSerializer.Deserialize<TaskEditRequestPayload>(r.PayloadJson)!;
            var requesterName = names.GetValueOrDefault(r.RequestedByEmployeeId) ?? "A teammate";
            return new TaskEditRequestResponse(
                r.Id,
                r.TaskId,
                r.Status,
                payload,
                requesterName,
                r.CreatedAt);
        }).ToList();

        return Result<IReadOnlyList<TaskEditRequestResponse>>.Success(items);
    }
}
