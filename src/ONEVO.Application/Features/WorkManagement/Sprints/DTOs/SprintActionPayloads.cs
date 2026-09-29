using System.Text.Json;

namespace ONEVO.Application.Features.WorkManagement.Sprints.DTOs;

/// <summary>payload_json of sprint.create.</summary>
public sealed record SprintCreateInput(Guid ProjectId, string Name, string? Goal, IReadOnlyList<Guid> TaskIds);

/// <summary>payload_json of sprint.edit.</summary>
public sealed record SprintEditInput(string Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate);

/// <summary>payload_json of sprint.start.</summary>
public sealed record SprintStartInput(DateOnly StartDate, DateOnly EndDate, string? Goal);

/// <summary>payload_json of sprint.complete. Disposition is "backlog" or "sprint" (then TargetSprintId is required).</summary>
public sealed record SprintCompleteInput(string Disposition, Guid? TargetSprintId);

public static class SprintPayloadJson
{
    /// <summary>Writes camelCase; reads case-insensitively.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
