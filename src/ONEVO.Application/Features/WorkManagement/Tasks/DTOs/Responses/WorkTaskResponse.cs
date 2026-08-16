using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record WorkTaskResponse(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    string TaskType, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent);

public sealed record TaskCreationRequestResponse(
    Guid Id, Guid ObjectiveId, string Status, TaskCreationRequestPayload Payload, DateTimeOffset CreatedAt);

/// <summary>Returned alongside a 409 slack-conflict so the frontend can offer the extend-allocation flow (spec §3.2).</summary>
public sealed record InsufficientAllocationResponse(decimal AvailableSlackHours, string SuggestedAction = "extend_allocation");
