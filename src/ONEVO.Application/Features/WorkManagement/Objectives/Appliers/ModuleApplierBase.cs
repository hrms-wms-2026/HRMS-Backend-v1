using System.Text.Json;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Appliers;

/// <summary>
/// Shared shape of the module.* appliers: load the Module tracked; a missing or deleted Module, or
/// one changed after the request was made (TargetUpdatedAtSnapshot), is Stale; a validation failure
/// from IModuleWriteService is Invalid (the request stays pending).
/// </summary>
public abstract class ModuleApplierBase : IApprovalActionApplier
{
    protected ModuleApplierBase(IObjectiveRepository objectives, IModuleWriteService modules)
    {
        Objectives = objectives;
        Modules = modules;
    }

    protected IObjectiveRepository Objectives { get; }
    protected IModuleWriteService Modules { get; }

    public abstract string ActionType { get; }

    /// <summary>False for actions whose request may legitimately outlive other edits (allocation extend).</summary>
    protected virtual bool ChecksSnapshot => true;

    /// <summary>Extra "nothing left to do" rule, e.g. achieving a Module that is already achieved.</summary>
    protected virtual bool IsStale(Objective module) => false;

    protected abstract Task<Result> ApplyAsync(Guid tenantId, Objective module, string payloadJson, CancellationToken ct);

    public async Task<ApplyOutcome> ApplyAsync(ApprovalApplyContext context, CancellationToken ct)
    {
        var request = context.Request;
        if (request.TargetId is null)
            return ApplyOutcome.Stale;

        var module = await Objectives.GetTrackedByIdForTenantAsync(request.TenantId, request.TargetId.Value, ct);
        if (module is null || !module.IsActive || IsStale(module))
            return ApplyOutcome.Stale;
        if (ChecksSnapshot && request.TargetUpdatedAtSnapshot is { } snap && (module.UpdatedAt ?? module.CreatedAt) > snap)
            return ApplyOutcome.Stale;

        var result = await ApplyAsync(request.TenantId, module, context.PayloadJson, ct);
        return result.IsSuccess ? ApplyOutcome.Applied : ApplyOutcome.Invalid(result.Error ?? "The change could not be applied.");
    }

    protected static T? Read<T>(string payloadJson) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(payloadJson, ModulePayloadJson.Options); }
        catch (JsonException) { return null; }
    }
}
