using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.Helpers;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

public sealed class ModuleWriteService : IModuleWriteService
{
    private readonly IObjectiveRepository _objectives;
    private readonly ISprintRepository _sprints;
    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IObjectiveAllocationSlackCalculator _slack;

    public ModuleWriteService(
        IObjectiveRepository objectives, ISprintRepository sprints, IMilestoneMembershipCoordinator membership,
        IObjectiveAllocationSlackCalculator slack)
    {
        _objectives = objectives;
        _sprints = sprints;
        _membership = membership;
        _slack = slack;
    }

    // ---- Edit ----

    public async Task<Result> ValidateEditAsync(Guid tenantId, Objective module, ModuleEditInput input, CancellationToken ct = default)
    {
        if (!module.IsActive)
            return Result.NotFound("Objective not found.");

        // Default-Objective carve-out (design §5) - edited only via PUT /projects/{id}.
        if (module.IsDefault)
            return Result.Failure("Use the Project edit endpoint for the Default Objective.");

        if (module.IsAchieved)
            return Result.Failure("An achieved milestone cannot be edited.");

        // Every non-default Objective always has a parent.
        if (module.ParentObjectiveId is null)
            return Result.NotFound("Parent objective not found.");

        var parent = await _objectives.GetByIdForTenantAsync(tenantId, module.ParentObjectiveId.Value, ct);
        if (parent is null)
            return Result.NotFound("Parent objective not found.");

        if (ObjectiveParentConstraintChecker.Conflicts(parent, input.StartDate, input.EndDate, input.AllocatedHours))
            return Result.Conflict("The edited date range or allocated hours would exceed the parent milestone's.");

        return Result.Success();
    }

    public async Task<Result> ApplyEditAsync(Guid tenantId, Objective trackedModule, ModuleEditInput input, CancellationToken ct = default)
    {
        var validation = await ValidateEditAsync(tenantId, trackedModule, input, ct);
        if (!validation.IsSuccess)
            return validation;

        trackedModule.Title = input.Title.Trim();
        trackedModule.Description = input.Description?.Trim();
        trackedModule.StartDate = input.StartDate;
        trackedModule.EndDate = input.EndDate;
        trackedModule.AllocatedHours = input.AllocatedHours;
        trackedModule.UpdatedAt = DateTimeOffset.UtcNow;
        _objectives.Update(trackedModule);
        return Result.Success();
    }

    // ---- Delete ----

    public Task<Result> ValidateDeleteAsync(Guid tenantId, Objective module, CancellationToken ct = default)
    {
        if (module.IsDefault)
            return Task.FromResult(Result.Failure("Use the Project delete endpoint for the Default Objective."));

        if (!module.IsActive)
            return Task.FromResult(Result.Conflict("Objective already deleted."));

        return Task.FromResult(Result.Success());
    }

    public async Task<Result> ApplyDeleteAsync(Guid tenantId, Objective trackedModule, CancellationToken ct = default)
    {
        var validation = await ValidateDeleteAsync(tenantId, trackedModule, ct);
        if (!validation.IsSuccess)
            return validation;

        trackedModule.IsActive = false;
        trackedModule.UpdatedAt = DateTimeOffset.UtcNow;
        _objectives.Update(trackedModule);
        return Result.Success();
    }

    // ---- Transfer head ----

    public async Task<Result> ValidateTransferAsync(Guid tenantId, Objective module, ModuleTransferInput input, CancellationToken ct = default)
    {
        if (!module.IsActive)
            return Result.NotFound("Objective not found.");

        if (module.IsDefault)
            return Result.Failure("The Default Objective's head cannot be transferred.");

        if (module.IsAchieved)
            return Result.Failure("An achieved milestone's head cannot be transferred.");

        var newHead = await _membership.GetActiveAssigneeAsync(tenantId, input.NewHeadEmployeeId, ct);
        if (newHead is null)
            return Result.Failure("The new head must be an active employee in this tenant.");

        return Result.Success();
    }

    public async Task<Result> ApplyTransferAsync(Guid tenantId, Objective trackedModule, ModuleTransferInput input, CancellationToken ct = default)
    {
        var validation = await ValidateTransferAsync(tenantId, trackedModule, input, ct);
        if (!validation.IsSuccess)
            return validation;

        var now = DateTimeOffset.UtcNow;
        var oldHeadEmployeeId = trackedModule.OwnerId;

        trackedModule.OwnerId = input.NewHeadEmployeeId;
        trackedModule.UpdatedAt = now;
        _objectives.Update(trackedModule);

        var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, trackedModule.Id, ct);
        foreach (var child in directChildren)
        {
            child.ReportingManagerId = input.NewHeadEmployeeId;
            child.UpdatedAt = now;
        }

        await _membership.UpsertMembershipAsync(tenantId, trackedModule.ProjectId, trackedModule.Id, input.NewHeadEmployeeId, ct);
        await _membership.DeactivateMembershipAsync(tenantId, trackedModule.ProjectId, trackedModule.Id, oldHeadEmployeeId, ct);
        await _membership.HasOtherActiveAccessAsync(tenantId, trackedModule.ProjectId, oldHeadEmployeeId, trackedModule.Id, ct);
        return Result.Success();
    }

    // ---- Achieve ----

    public async Task<Result> ValidateAchieveAsync(Guid tenantId, Objective module, CancellationToken ct = default)
    {
        if (!module.IsActive)
            return Result.NotFound("Objective not found.");

        if (module.IsDefault)
            return Result.Failure("Use the Project achieve endpoint for the Default Objective.");

        if (module.IsAchieved)
            return Result.Conflict("Objective is already achieved.");

        // Precondition (design §6): every direct child must already be achieved. Shallow check -
        // grandchildren are covered transitively, since a child can't itself be achieved until
        // ITS children are.
        var directChildren = await _objectives.GetTrackedActiveDirectChildrenAsync(tenantId, module.Id, ct);
        if (directChildren.Any(c => !c.IsAchieved))
            return Result.Failure("All sub-milestones must be achieved before this one can be.");

        // A module is blocked while any of its tasks sits in an Active sprint (sprints are project-level now).
        if (await _sprints.AnyActiveContainingObjectiveTasksAsync(tenantId, module.Id, ct))
            return Result.Failure("Tasks of this milestone are still in an Active sprint - complete that sprint first.");

        return Result.Success();
    }

    public async Task<Result> ApplyAchieveAsync(Guid tenantId, Objective trackedModule, CancellationToken ct = default)
    {
        var validation = await ValidateAchieveAsync(tenantId, trackedModule, ct);
        if (!validation.IsSuccess)
            return validation;

        var now = DateTimeOffset.UtcNow;
        trackedModule.IsAchieved = true;
        trackedModule.AchievedAt = now;
        trackedModule.UpdatedAt = now;
        _objectives.Update(trackedModule);

        // Freezing drops the Head's active participation on this milestone (design §6) - same
        // outgoing-access pattern as Transfer, just with no new Head to upsert a membership for.
        await _membership.DeactivateMembershipAsync(tenantId, trackedModule.ProjectId, trackedModule.Id, trackedModule.OwnerId, ct);
        await _membership.HasOtherActiveAccessAsync(tenantId, trackedModule.ProjectId, trackedModule.OwnerId, trackedModule.Id, ct);
        return Result.Success();
    }

    // ---- Unachieve ----

    public async Task<Result> ValidateUnachieveAsync(Guid tenantId, Objective module, CancellationToken ct = default)
    {
        if (!module.IsActive)
            return Result.NotFound("Objective not found.");

        if (module.IsDefault)
            return Result.Failure("Use the Project achieve endpoint for the Default Objective.");

        if (!module.IsAchieved)
            return Result.Conflict("Objective is not achieved.");

        var head = await _membership.GetActiveAssigneeAsync(tenantId, module.OwnerId, ct);
        if (head is null)
            return Result.Failure("The current head must be an active employee in this tenant.");

        return Result.Success();
    }

    public async Task<Result> ApplyUnachieveAsync(Guid tenantId, Objective trackedModule, CancellationToken ct = default)
    {
        var validation = await ValidateUnachieveAsync(tenantId, trackedModule, ct);
        if (!validation.IsSuccess)
            return validation;

        trackedModule.IsAchieved = false;
        trackedModule.AchievedAt = null;
        trackedModule.UpdatedAt = DateTimeOffset.UtcNow;
        _objectives.Update(trackedModule);

        // Un-freezing restores the Head's active participation, mirroring Achieve's cleanup in reverse.
        await _membership.UpsertMembershipAsync(tenantId, trackedModule.ProjectId, trackedModule.Id, trackedModule.OwnerId, ct);
        return Result.Success();
    }

    // ---- Allocation extend ----

    public Task<Result> ValidateAllocationExtendAsync(Guid tenantId, Objective module, ModuleAllocationExtendInput input, CancellationToken ct = default)
    {
        if (!module.IsActive)
            return Task.FromResult(Result.NotFound("Objective not found."));

        if (module.ParentObjectiveId is null)
            return Task.FromResult(Result.Failure(
                "This milestone has no Reporting Manager to route to - it is a top-level milestone. Edit the Project directly instead.", 400));

        if (input.RequestedAdditionalHours <= 0m)
            return Task.FromResult(Result.Failure("Approved additional hours must be greater than zero.", 400));

        return Task.FromResult(Result.Success());
    }

    public async Task<Result> ApplyAllocationExtendAsync(Guid tenantId, Objective trackedModule, ModuleAllocationExtendInput input, CancellationToken ct = default)
    {
        var validation = await ValidateAllocationExtendAsync(tenantId, trackedModule, input, ct);
        if (!validation.IsSuccess)
            return validation;

        var parent = await _objectives.GetByIdForTenantAsync(tenantId, trackedModule.ParentObjectiveId!.Value, ct);
        if (parent is null)
            return Result.Failure("Approver's own milestone could not be resolved.", 422);

        var parentSlack = await _slack.CalculateAsync(tenantId, parent, ct: ct);
        if (input.RequestedAdditionalHours > parentSlack)
            return Result.Conflict(
                "You don't have enough allocation yourself to approve this. Request more from your own reporting manager first, then return to approve this request.");

        trackedModule.AllocatedHours += input.RequestedAdditionalHours;
        trackedModule.UpdatedAt = DateTimeOffset.UtcNow;
        _objectives.Update(trackedModule);
        return Result.Success();
    }
}
