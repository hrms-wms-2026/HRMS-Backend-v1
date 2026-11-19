using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Appliers;

/// <summary>Applies an approved module.member_remove via the coordinator, which is also what the
/// direct-apply path calls.</summary>
public sealed class ModuleMemberRemoveApplier : ModuleApplierBase
{
    private readonly IMilestoneMembershipCoordinator _membership;

    public ModuleMemberRemoveApplier(IObjectiveRepository objectives, IModuleWriteService modules, IMilestoneMembershipCoordinator membership)
        : base(objectives, modules) => _membership = membership;

    public override string ActionType => WorkActionTypes.ModuleMemberRemove;

    protected override bool IsStale(Objective module) => module.IsAchieved;

    protected override Task<Result> ApplyAsync(Guid tenantId, Objective module, string payloadJson, CancellationToken ct)
    {
        var input = Read<ModuleMemberRemoveInput>(payloadJson);
        if (input is null || input.EmployeeId == Guid.Empty)
            return Task.FromResult(Result.Failure("The member-remove request has no employee."));

        return _membership.ApplyMemberRemoveAsync(tenantId, module, input.EmployeeId, ct);
    }
}
