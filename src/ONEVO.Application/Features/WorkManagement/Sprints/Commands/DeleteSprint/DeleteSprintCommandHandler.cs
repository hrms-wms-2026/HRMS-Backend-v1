using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.DeleteSprint;

/// <summary>
/// Deletes a Complete or Achieved sprint (Draft/Active → 409). The sprint's creator, or anyone at
/// or above its creator position, deletes it now; other project members file a sprint.delete
/// request. Tasks still in the sprint go back to the backlog.
/// </summary>
public class DeleteSprintCommandHandler : IRequestHandler<DeleteSprintCommand, Result<SprintWriteOutcome>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISprintRepository _sprints;
    private readonly IProjectMemberRepository _members;
    private readonly ISprintWriteService _writes;
    private readonly ISprintActionSubmitter _submitter;

    public DeleteSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, ISprintRepository sprints, IProjectMemberRepository members,
        ISprintWriteService writes, ISprintActionSubmitter submitter)
    {
        _currentUser = currentUser;
        _identity = identity;
        _sprints = sprints;
        _members = members;
        _writes = writes;
        _submitter = submitter;
    }

    public async Task<Result<SprintWriteOutcome>> Handle(DeleteSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintWriteOutcome>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<SprintWriteOutcome>.Forbidden("No employee record for the current user.");

        var sprint = await _sprints.GetTrackedByIdForTenantAsync(tenantId, request.SprintId, ct);
        if (sprint is null)
            return Result<SprintWriteOutcome>.NotFound("Sprint not found.");

        if (!await _members.HasActiveMembershipAsync(tenantId, sprint.ProjectId, callerEmployeeId.Value, ct))
            return Result<SprintWriteOutcome>.Forbidden("Only project members can change sprints.");

        var validation = _writes.ValidateDelete(sprint);
        if (!validation.IsSuccess)
            return Result<SprintWriteOutcome>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var outcome = await _submitter.SubmitAsync(
            new SprintActionRequest(tenantId, callerEmployeeId.Value, sprint.ProjectId, sprint, WorkActionTypes.SprintDelete, sprint.Name, null,
                // The sprint's own creator deletes without asking (user decision 2026-09-28).
                ForceDirect: sprint.CreatedById == _currentUser.UserId),
            async innerCt =>
            {
                await _writes.ApplyDeleteAsync(tenantId, sprint, innerCt);
                return Result<Sprint?>.Success(null);
            }, ct);

        return SprintOutcomes.ToWriteOutcome(outcome);
    }
}
