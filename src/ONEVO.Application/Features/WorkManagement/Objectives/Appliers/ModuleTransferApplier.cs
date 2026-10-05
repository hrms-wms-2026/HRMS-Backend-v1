using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Appliers;

/// <summary>Applies an approved module.transfer. Stale if the Module changed after the request.</summary>
public sealed class ModuleTransferApplier : ModuleApplierBase
{
    public ModuleTransferApplier(IObjectiveRepository objectives, IModuleWriteService modules) : base(objectives, modules) { }

    public override string ActionType => WorkActionTypes.ModuleTransfer;

    protected override async Task<Result> ApplyAsync(Guid tenantId, Objective module, string payloadJson, CancellationToken ct)
    {
        var input = Read<ModuleTransferInput>(payloadJson);
        if (input is null || input.NewHeadEmployeeId == Guid.Empty)
            return Result.Failure("The transfer request has no new head.");
        return await Modules.ApplyTransferAsync(tenantId, module, input, ct);
    }

    protected override string? CaptureUndoJson(Objective module)
        => System.Text.Json.JsonSerializer.Serialize(new ModuleTransferInput(module.OwnerId), ModulePayloadJson.Options);
}
