using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Appliers;

/// <summary>Applies an approved sprint.start with the requested (or approver-edited) dates.</summary>
public sealed class SprintStartApplier : SprintApplierBase
{
    public SprintStartApplier(ISprintRepository sprints, ISprintWriteService writes) : base(sprints, writes) { }

    public override string ActionType => WorkActionTypes.SprintStart;

    protected override async Task<Result> ApplyAsync(Guid tenantId, Guid actorEmployeeId, Sprint sprint, string payloadJson, CancellationToken ct)
    {
        var input = Read<SprintStartInput>(payloadJson);
        if (input is null)
            return Result.Failure("The sprint start request has no dates.");
        return await Writes.ApplyStartAsync(tenantId, actorEmployeeId, sprint, input, ct);
    }

    protected override string? CaptureUndoJson(Sprint sprint)
        // Always snapshot, even when Goal is null - a null UndoJson must mean "no snapshot was taken"
        // (legacy data from before this feature), not "the pre-start Goal was null". SprintStartReverter
        // relies on this: a present-but-null-Goal snapshot clears Goal back to null on revert.
        => System.Text.Json.JsonSerializer.Serialize(new SprintStartUndoSnapshot(sprint.Goal), SprintPayloadJson.Options);
}
