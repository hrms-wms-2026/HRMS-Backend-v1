using System.Text.Json;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Appliers;

public static class TaskPayload
{
    // Case-insensitive: rows migrated from the old request tables were serialised PascalCase.
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Applies an approved task.create: the requester becomes the task's creator and the target Module is its creator position.</summary>
public sealed class TaskCreateApplier : IApprovalActionApplier
{
    private readonly ITaskWriteService _writes;
    private readonly IObjectiveRepository _objectives;
    private readonly IMilestoneMembershipCoordinator _membership;

    public TaskCreateApplier(ITaskWriteService writes, IObjectiveRepository objectives, IMilestoneMembershipCoordinator membership)
    {
        _writes = writes;
        _objectives = objectives;
        _membership = membership;
    }

    public string ActionType => WorkActionTypes.TaskCreate;

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        var input = JsonSerializer.Deserialize<TaskCreateInput>(context.PayloadJson, TaskPayload.Options);
        if (input is null || string.IsNullOrWhiteSpace(input.Title))
            return ApplyOutcome.Invalid("The task request has no title.");

        var objective = await _objectives.GetByIdForTenantAsync(request.TenantId, input.ObjectiveId, ct);
        if (objective is null || !objective.IsActive)
            return ApplyOutcome.Stale;

        var requester = await _membership.GetActiveAssigneeAsync(request.TenantId, request.RequestedByEmployeeId, ct);
        if (requester is null)
            return ApplyOutcome.Stale;

        var created = await _writes.CreateAsync(request.TenantId, requester.UserId, request.RequestedByEmployeeId,
            input, input.ObjectiveId, ct);
        if (!created.IsSuccess)
            return ApplyOutcome.Invalid(created.Error ?? "The task could not be created.");

        request.TargetId = created.Value!.Id;   // links the approved request to the task it produced
        return ApplyOutcome.Applied();
    }
}
