using System.Text.Json;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Appliers;

/// <summary>
/// Shared shape of the sprint.* appliers for an existing sprint: load it tracked; a missing sprint,
/// or one changed after the request was made (TargetUpdatedAtSnapshot), is Stale; a validation
/// failure from ISprintWriteService is Invalid (the request stays pending). The requester is the
/// actor in the sprint's activity log.
/// </summary>
public abstract class SprintApplierBase : IApprovalActionApplier
{
    protected SprintApplierBase(ISprintRepository sprints, ISprintWriteService writes)
    {
        Sprints = sprints;
        Writes = writes;
    }

    protected ISprintRepository Sprints { get; }
    protected ISprintWriteService Writes { get; }

    public abstract string ActionType { get; }

    protected abstract Task<Result> ApplyAsync(Guid tenantId, Guid actorEmployeeId, Sprint sprint, string payloadJson, CancellationToken ct);

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return ApplyOutcome.Stale;

        var sprint = await Sprints.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (sprint is null)
            return ApplyOutcome.Stale;
        if (request.TargetUpdatedAtSnapshot is { } snap && (sprint.UpdatedAt ?? sprint.CreatedAt) > snap)
            return ApplyOutcome.Stale;

        var result = await ApplyAsync(request.TenantId, request.RequestedByEmployeeId, sprint, context.PayloadJson, ct);
        return result.IsSuccess ? ApplyOutcome.Applied() : ApplyOutcome.Invalid(result.Error ?? "The sprint change could not be applied.");
    }

    protected static T? Read<T>(string payloadJson) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(payloadJson, SprintPayloadJson.Options); }
        catch (JsonException) { return null; }
    }
}
