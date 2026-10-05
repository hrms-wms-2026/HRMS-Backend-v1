using System.Text.Json;

namespace ONEVO.Application.Features.WorkManagement.Sprints.DTOs;

/// <summary>payload_json of sprint.create.</summary>
public sealed record SprintCreateInput(Guid ProjectId, string Name, string? Goal, IReadOnlyList<Guid> TaskIds);

/// <summary>payload_json of sprint.edit.</summary>
public sealed record SprintEditInput(string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>payload_json of sprint.start.</summary>
public sealed record SprintStartInput(DateOnly StartDate, DateOnly EndDate, string? Goal);

/// <summary>UndoStateJson for sprint.start - only the pre-start Goal; StartDate/EndDate always revert
/// to null (a Draft sprint has no dates by definition).</summary>
public sealed record SprintStartUndoSnapshot(string? PreviousGoal);

/// <summary>payload_json of sprint.complete. Disposition is "backlog" or "sprint" (then TargetSprintId is required).</summary>
public sealed record SprintCompleteInput(string Disposition, Guid? TargetSprintId);

/// <summary>UndoStateJson for sprint.achieve - the status the sprint had right before it was achieved.</summary>
public sealed record SprintAchieveUndoSnapshot(string PreviousStatus);

/// <summary>UndoStateJson for sprint.delete - the tasks ApplyDeleteAsync detached, to reattach on revert.</summary>
public sealed record SprintDeleteUndoSnapshot(IReadOnlyList<Guid> TaskIds);

public static class SprintPayloadJson
{
    /// <summary>Writes camelCase; reads case-insensitively.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
