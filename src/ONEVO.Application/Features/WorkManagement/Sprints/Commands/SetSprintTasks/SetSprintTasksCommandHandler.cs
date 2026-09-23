using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.SetSprintTasks;

/// <summary>Spec D2: no CanManage gate here - the per-task module-ownership check inside
/// ISprintTaskAssignmentService.PrepareAsync IS the gate (a module owner may put their tasks into
/// anyone's sprint). CanManage on the response is computed with ISprintAccessService after the change.</summary>
public class SetSprintTasksCommandHandler : IRequestHandler<SetSprintTasksCommand, Result<SprintResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISprintRepository _sprints;
    private readonly ISprintTaskAssignmentService _assignment;
    private readonly ISprintAccessService _access;
    private readonly ISprintActivityLogRepository _logs;
    private readonly IUnitOfWork _unitOfWork;

    public SetSprintTasksCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ISprintRepository sprints,
        ISprintTaskAssignmentService assignment, ISprintAccessService access, ISprintActivityLogRepository logs, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sprints = sprints;
        _assignment = assignment;
        _access = access;
        _logs = logs;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<SprintResponse>> Handle(SetSprintTasksCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<SprintResponse>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintResponse>.NotFound("Sprint not found.");

        var prepared = await _assignment.PrepareAsync(tenantId, sprint, request.AddTaskIds, request.RemoveTaskIds, callerEmployeeId.Value, ct);
        if (!prepared.IsSuccess)
            return Result<SprintResponse>.Failure(prepared.Error!, prepared.StatusCode ?? 400);
        var changes = prepared.Value!;

        if (changes.IsEmpty)
        {
            var canManageNoChange = await _access.CanManageAsync(tenantId, sprint, userId, callerEmployeeId.Value, ct);
            return Result<SprintResponse>.Success(SprintResponse.From(sprint, canManageNoChange));
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _assignment.Apply(changes, sprint.Id);

            if (changes.ToAdd.Count > 0)
                await _logs.AddAsync(SprintActivityLogFactory.Create(
                    tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.TasksAdded,
                    details: new { taskIds = changes.ToAdd.Select(t => t.Id) }), innerCt);
            if (changes.ToRemove.Count > 0)
                await _logs.AddAsync(SprintActivityLogFactory.Create(
                    tenantId, sprint.Id, callerEmployeeId.Value, SprintActivityActions.TasksRemoved,
                    details: new { taskIds = changes.ToRemove.Select(t => t.Id) }), innerCt);

            await _unitOfWork.SaveChangesAsync(innerCt);

            var canManage = await _access.CanManageAsync(tenantId, sprint, userId, callerEmployeeId.Value, innerCt);
            return Result<SprintResponse>.Success(SprintResponse.From(sprint, canManage));
        }, ct);
    }
}
