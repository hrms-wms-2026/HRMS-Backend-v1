using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

/// <summary>
/// The single home of Sprint action validation and mutation, shared by the direct command handlers
/// and the approval appliers. Permission checks and saving stay in callers. Every Validate* is
/// read-only; every Apply* re-validates first (an approval may come much later).
/// </summary>
public interface ISprintWriteService
{
    Task<Result> ValidateCreateAsync(Guid tenantId, Guid actorEmployeeId, SprintCreateInput input, CancellationToken ct = default);
    Task<Result<Sprint>> CreateAsync(Guid tenantId, Guid creatorUserId, Guid actorEmployeeId, SprintCreateInput input,
        Guid creatorPositionObjectiveId, CancellationToken ct = default);

    Task<Result> ValidateEditAsync(Sprint sprint, SprintEditInput input);
    Task<Result> ApplyEditAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintEditInput input, CancellationToken ct = default);

    Task<Result> ValidateStartAsync(Guid tenantId, Sprint sprint, SprintStartInput input, CancellationToken ct = default);
    Task<Result> ApplyStartAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintStartInput input, CancellationToken ct = default);

    Task<Result> ValidateCompleteAsync(Guid tenantId, Sprint sprint, SprintCompleteInput input, CancellationToken ct = default);
    Task<Result<IReadOnlyList<Guid>>> ApplyCompleteAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, SprintCompleteInput input, CancellationToken ct = default);

    Task<Result> ValidateAchieveAsync(Guid tenantId, Sprint sprint, CancellationToken ct = default);
    Task<Result> ApplyAchieveAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, CancellationToken ct = default);

    /// <summary>Only a Complete or Achieved sprint may be deleted.</summary>
    Result ValidateDelete(Sprint sprint);
    /// <summary>Its tasks go back to the backlog; the sprint row is soft-deleted.</summary>
    Task ApplyDeleteAsync(Guid tenantId, Sprint trackedSprint, CancellationToken ct = default);

    /// <summary>Undoes ApplyDeleteAsync: clears the soft-delete flags and reattaches the given tasks to
    /// this sprint (the ones ApplyDeleteAsync detached). Conflict (nothing applied) if any of those
    /// tasks has since been attached to a different sprint.</summary>
    Task<Result> Restore(Guid tenantId, Sprint trackedSprint, IReadOnlyCollection<Guid> taskIdsToReattach, CancellationToken ct = default);

    /// <summary>Undoes ApplyCompleteAsync: Active status, clears CompletedAt, and moves movedTaskIds back
    /// onto this sprint. Conflict (nothing applied) if a moved task is no longer where Complete left it
    /// (targetSprintId if Disposition was "sprint", else unassigned) - something else touched it since.</summary>
    Task<Result> ApplyUncompleteAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, IReadOnlyCollection<Guid> movedTaskIds, Guid? targetSprintId, CancellationToken ct = default);

    /// <summary>Undoes ApplyAchieveAsync: restores the status the sprint had right before it was
    /// achieved (Draft, Active or Complete - Achieve is allowed from any of those) and clears AchievedAt.</summary>
    Task<Result> ApplyUnachieveAsync(Guid tenantId, Guid actorEmployeeId, Sprint trackedSprint, string previousStatus, CancellationToken ct = default);
}
