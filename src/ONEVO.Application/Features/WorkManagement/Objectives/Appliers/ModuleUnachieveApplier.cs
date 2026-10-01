using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Appliers;

/// <summary>Applies an approved module.unachieve. Stale if the Module is no longer achieved or changed after the request.</summary>
public sealed class ModuleUnachieveApplier : ModuleApplierBase
{
    public ModuleUnachieveApplier(IObjectiveRepository objectives, IModuleWriteService modules) : base(objectives, modules) { }

    public override string ActionType => WorkActionTypes.ModuleUnachieve;

    protected override bool IsStale(Objective module) => !module.IsAchieved;

    protected override Task<Result> ApplyAsync(Guid tenantId, Objective module, string payloadJson, CancellationToken ct)
        => Modules.ApplyUnachieveAsync(tenantId, module, ct);
}
