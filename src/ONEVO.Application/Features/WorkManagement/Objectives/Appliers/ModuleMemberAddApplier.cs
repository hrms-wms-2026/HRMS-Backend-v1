using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Appliers;

/// <summary>Applies an approved module.member_add: re-validates and creates the invitation via the
/// coordinator, which is also what the direct-apply path calls.</summary>
public sealed class ModuleMemberAddApplier : ModuleApplierBase
{
    private readonly IMilestoneMembershipCoordinator _membership;

    public ModuleMemberAddApplier(IObjectiveRepository objectives, IModuleWriteService modules, IMilestoneMembershipCoordinator membership)
        : base(objectives, modules) => _membership = membership;

    public override string ActionType => WorkActionTypes.ModuleMemberAdd;

    protected override bool IsStale(Objective module) => module.IsAchieved;

    protected override async Task<Result> ApplyAsync(Guid tenantId, Objective module, string payloadJson, CancellationToken ct)
    {
        var input = Read<ModuleMemberAddInput>(payloadJson);
        if (input is null || input.EmployeeId == Guid.Empty)
            return Result.Failure("The member-add request has no employee.");

        var applied = await _membership.ApplyMemberAddAsync(tenantId, module, input.RequestedByEmployeeId, input.EmployeeId, ct);
        return applied.IsSuccess ? Result.Success() : Result.Failure(applied.Error!, applied.StatusCode ?? 400);
    }
}
