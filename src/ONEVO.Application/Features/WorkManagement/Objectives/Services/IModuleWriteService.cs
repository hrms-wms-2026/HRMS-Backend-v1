using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Objectives.DTOs;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;

namespace ONEVO.Application.Features.WorkManagement.Objectives.Services;

/// <summary>
/// The single home of Module (Objective) action validation and mutation, shared by the direct
/// command handlers and the approval appliers. Permission checks stay in callers. Never saves.
/// Every Validate* is read-only; every Apply* re-validates first (an approval may come much later).
/// </summary>
public interface IModuleWriteService
{
    Task<Result> ValidateEditAsync(Guid tenantId, Objective module, ModuleEditInput input, CancellationToken ct = default);
    Task<Result> ApplyEditAsync(Guid tenantId, Objective trackedModule, ModuleEditInput input, CancellationToken ct = default);

    Task<Result> ValidateDeleteAsync(Guid tenantId, Objective module, CancellationToken ct = default);
    Task<Result> ApplyDeleteAsync(Guid tenantId, Objective trackedModule, CancellationToken ct = default);

    /// <summary>Undoes ApplyDeleteAsync: Objective "delete" is the IsActive flag, not BaseEntity.IsDeleted.</summary>
    void Restore(Objective trackedModule);

    Task<Result> ValidateTransferAsync(Guid tenantId, Objective module, ModuleTransferInput input, CancellationToken ct = default);
    Task<Result> ApplyTransferAsync(Guid tenantId, Objective trackedModule, ModuleTransferInput input, CancellationToken ct = default);

    Task<Result> ValidateAchieveAsync(Guid tenantId, Objective module, CancellationToken ct = default);
    Task<Result> ApplyAchieveAsync(Guid tenantId, Objective trackedModule, CancellationToken ct = default);

    Task<Result> ValidateUnachieveAsync(Guid tenantId, Objective module, CancellationToken ct = default);
    Task<Result> ApplyUnachieveAsync(Guid tenantId, Objective trackedModule, CancellationToken ct = default);

    Task<Result> ValidateAllocationExtendAsync(Guid tenantId, Objective module, ModuleAllocationExtendInput input, CancellationToken ct = default);
    Task<Result> ApplyAllocationExtendAsync(Guid tenantId, Objective trackedModule, ModuleAllocationExtendInput input, CancellationToken ct = default);
}
