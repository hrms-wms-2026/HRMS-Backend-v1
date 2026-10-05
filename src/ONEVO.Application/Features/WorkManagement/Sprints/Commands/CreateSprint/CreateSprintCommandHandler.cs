using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Commands.CreateSprint;

/// <summary>
/// Creates a sprint through the approval engine. The sprint's creator position is the caller's
/// highest owned Module, else their highest member Module, else the project root; a caller at or
/// above it creates the sprint now, anyone else files a sprint.create request to its holder.
/// </summary>
public class CreateSprintCommandHandler : IRequestHandler<CreateSprintCommand, Result<SprintWriteOutcome>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IProjectRepository _projects;
    private readonly IProjectMemberRepository _members;
    private readonly IPermissionResolver _permissionResolver;
    private readonly ISprintWriteService _writes;
    private readonly ISprintActionSubmitter _submitter;

    public CreateSprintCommandHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IProjectRepository projects,
        IProjectMemberRepository members, IPermissionResolver permissionResolver, ISprintWriteService writes,
        ISprintActionSubmitter submitter)
    {
        _currentUser = currentUser;
        _identity = identity;
        _projects = projects;
        _members = members;
        _permissionResolver = permissionResolver;
        _writes = writes;
        _submitter = submitter;
    }

    public async Task<Result<SprintWriteOutcome>> Handle(CreateSprintCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<SprintWriteOutcome>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var userId = _currentUser.UserId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, userId, ct);
        if (callerEmployeeId is null)
            return Result<SprintWriteOutcome>.Forbidden("No employee record for the current user.");

        var project = await _projects.GetByIdForTenantAsync(tenantId, request.ProjectId, ct);
        if (project is null || !project.IsActive)
            return Result<SprintWriteOutcome>.NotFound("Project not found.");

        var permissions = await _permissionResolver.ResolveAsync(userId, tenantId, null, ct);
        var hasReadPermission = permissions.Contains("*");
        if (!hasReadPermission && !await _members.HasActiveMembershipAsync(tenantId, project.Id, callerEmployeeId.Value, ct))
            return Result<SprintWriteOutcome>.Forbidden("Only project members can create sprints.");

        var input = new SprintCreateInput(project.Id, request.Name.Trim(), request.Goal?.Trim(), request.TaskIds);
        var validation = await _writes.ValidateCreateAsync(tenantId, callerEmployeeId.Value, input, ct);
        if (!validation.IsSuccess)
            return Result<SprintWriteOutcome>.Failure(validation.Error!, validation.StatusCode ?? 400);

        var position = await _submitter.ResolveCreatorPositionAsync(tenantId, project.Id, callerEmployeeId.Value, ct);
        if (!position.IsSuccess)
            return Result<SprintWriteOutcome>.Failure(position.Error!, position.StatusCode ?? 400);

        var outcome = await _submitter.SubmitAsync(
            new SprintActionRequest(tenantId, callerEmployeeId.Value, project.Id, null, WorkActionTypes.SprintCreate, input.Name,
                input, CreatorPositionObjectiveId: position.Value),
            async innerCt =>
            {
                var created = await _writes.CreateAsync(tenantId, userId, callerEmployeeId.Value, input, position.Value, innerCt);
                return created.IsSuccess
                    ? Result<Sprint?>.Success(created.Value)
                    : Result<Sprint?>.Failure(created.Error!, created.StatusCode ?? 400);
            }, ct);

        return SprintOutcomes.ToWriteOutcome(outcome);
    }
}
