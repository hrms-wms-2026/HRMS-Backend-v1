using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Appliers;

/// <summary>Applies an approved module.allocation_extend with the (possibly lowered) hours from the approver-edited payload.</summary>
public sealed class ModuleAllocationExtendApplier : ModuleApplierBase
{
    public ModuleAllocationExtendApplier(IObjectiveRepository objectives, IModuleWriteService modules) : base(objectives, modules) { }

    public override string ActionType => WorkActionTypes.ModuleAllocationExtend;

    // Hours may legitimately change while the request waits, so no snapshot check; the parent-slack
    // check in ApplyAllocationExtendAsync is what protects the tree.
    protected override bool ChecksSnapshot => false;

    protected override async Task<Result> ApplyAsync(Guid tenantId, Objective module, string payloadJson, CancellationToken ct)
    {
        var input = Read<ModuleAllocationExtendInput>(payloadJson);
        if (input is null)
            return Result.Failure("The allocation request has no hours.");
        return await Modules.ApplyAllocationExtendAsync(tenantId, module, input, ct);
    }
}
