using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs;
using ONEVO.Application.Features.WorkManagement.Sprints.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Sprints.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Sprints.Entities;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Appliers;

/// <summary>Applies an approved sprint.complete with its task disposition. Snapshots the moved task
/// ids after ApplyCompleteAsync runs (they don't exist until then) for SprintCompleteReverter.</summary>
public sealed class SprintCompleteApplier : SprintApplierBase
{
    private IReadOnlyList<Guid>? _movedTaskIds;

    public SprintCompleteApplier(ISprintRepository sprints, ISprintWriteService writes) : base(sprints, writes) { }

    public override string ActionType => WorkActionTypes.SprintComplete;

    protected override async Task<Result> ApplyAsync(Guid tenantId, Guid actorEmployeeId, Sprint sprint, string payloadJson, CancellationToken ct)
    {
        var input = Read<SprintCompleteInput>(payloadJson);
        if (input is null)
            return Result.Failure("The sprint complete request has no disposition.");
        var applied = await Writes.ApplyCompleteAsync(tenantId, actorEmployeeId, sprint, input, ct);
        if (applied.IsSuccess)
            _movedTaskIds = applied.Value;
        return applied.IsSuccess ? Result.Success() : Result.Failure(applied.Error!, applied.StatusCode ?? 400);
    }

    protected override string? CaptureUndoJsonAfterApply(Sprint sprint)
        => _movedTaskIds is null ? null : System.Text.Json.JsonSerializer.Serialize(new SprintCompleteUndoSnapshot(_movedTaskIds), SprintPayloadJson.Options);
}
