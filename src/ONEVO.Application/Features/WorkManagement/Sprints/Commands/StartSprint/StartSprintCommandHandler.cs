using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.StartSprint;

/// <summary>Starts a Draft sprint through the approval engine: at or above its creator position → now; other project members → sprint.start request.</summary>
public class StartSprintCommandHandler : IRequestHandler<StartSprintCommand, Result<SprintWriteOutcome>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly ISprintRepository _sprints;
    private readonly IProjectMemberRepository _members;
    private readonly ISprintWriteService _writes;
    private readonly ISprintActionSubmitter _submitter;

    public StartSprintCommandHandler(
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

    public async Task<Result<SprintWriteOutcome>> Handle(StartSprintCommand request, CancellationToken ct)
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

        var input = new SprintStartInput(request.StartDate, request.EndDate, request.Goal?.Trim());
        var validation = await _writes.ValidateStartAsync(tenantId, sprint, input, ct);
        if (!validation.IsSuccess)
            return Result<SprintWriteOutcome>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var outcome = await _submitter.SubmitAsync(
            new SprintActionRequest(tenantId, callerEmployeeId.Value, sprint.ProjectId, sprint, WorkActionTypes.SprintStart, sprint.Name, input),
            async innerCt =>
            {
                var applied = await _writes.ApplyStartAsync(tenantId, callerEmployeeId.Value, sprint, input, innerCt);
                return applied.IsSuccess ? Result<Sprint?>.Success(sprint) : Result<Sprint?>.Failure(applied.Error!, applied.StatusCode ?? 400);
            }, ct);

        return SprintOutcomes.ToWriteOutcome(outcome);
    }
}
