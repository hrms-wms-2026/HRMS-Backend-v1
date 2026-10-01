using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Appliers;

/// <summary>Applies an approved module.edit (the approver may have adjusted the fields). Stale if the Module changed after the request.</summary>
public sealed class ModuleEditApplier : ModuleApplierBase
{
    public ModuleEditApplier(IObjectiveRepository objectives, IModuleWriteService modules) : base(objectives, modules) { }

    public override string ActionType => WorkActionTypes.ModuleEdit;

    protected override async Task<Result> ApplyAsync(Guid tenantId, Objective module, string payloadJson, CancellationToken ct)
    {
        var input = Read<ModuleEditInput>(payloadJson);
        if (input is null || string.IsNullOrWhiteSpace(input.Title))
            return Result.Failure("The edit request has no title.");
        return await Modules.ApplyEditAsync(tenantId, module, input, ct);
    }
}
