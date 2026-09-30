using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

/// <summary>Serialised as wm_approval_requests.payload_json for task.create. ObjectiveId = target Module.</summary>
public sealed record TaskCreateInput(
    Guid ObjectiveId, string Title, string? Description, Guid CategoryId, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid? SprintId);

/// <summary>Serialised as payload_json for task.edit. SprintId null = leave the sprint alone.</summary>
public sealed record TaskEditInput(
    string Title, string? Description, string Priority, DateOnly? DueDate, decimal? EstimatedHours,
    int? StoryPoints, int? ProgressPercent, string? Reason, Guid? SprintId);

/// <summary>Exactly one of Task / ApprovalRequestId is set: applied now, or sent for approval.</summary>
public sealed record TaskWriteOutcome(WorkTaskResponse? Task, Guid? ApprovalRequestId);
