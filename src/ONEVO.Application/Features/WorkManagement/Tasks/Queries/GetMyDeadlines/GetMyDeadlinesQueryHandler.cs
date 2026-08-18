using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyDeadlines;

public class GetMyDeadlinesQueryHandler : IRequestHandler<GetMyDeadlinesQuery, Result<MyDeadlinesResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IObjectiveRepository _objectives;
    private readonly IWorkTaskRepository _tasks;

    public GetMyDeadlinesQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IObjectiveRepository objectives, IWorkTaskRepository tasks)
    {
        _currentUser = currentUser;
        _identity = identity;
        _objectives = objectives;
        _tasks = tasks;
    }

    public async Task<Result<MyDeadlinesResponse>> Handle(GetMyDeadlinesQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MyDeadlinesResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<MyDeadlinesResponse>.Forbidden("No employee record for the current user.");

        var objectives = await _objectives.GetOwnedByEmployeeIdWithinRangeAsync(
            tenantId, callerEmployeeId.Value, request.From, request.To, ct);
        var tasks = await _tasks.GetAssignedToEmployeeWithinRangeAsync(
            tenantId, callerEmployeeId.Value, request.From, request.To, ct);

        return Result<MyDeadlinesResponse>.Success(new MyDeadlinesResponse(
            objectives.Select(o => new ObjectiveDeadlineItem(o.Id, o.Title, o.EndDate)).ToList(),
            tasks.Where(t => t.DueDate.HasValue)
                .Select(t => new TaskDeadlineItem(t.Id, t.ShortId, t.Title, t.DueDate!.Value)).ToList()));
    }
}
