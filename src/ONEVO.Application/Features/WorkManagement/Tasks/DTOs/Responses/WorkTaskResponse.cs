using ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record WorkTaskResponse(
    Guid Id, Guid ObjectiveId, string ShortId, string Title, string? Description,
    string TaskType, Guid StatusId, string Priority, int? StoryPoints,
    DateOnly? DueDate, decimal? EstimatedHours, decimal CompletedHours, int ProgressPercent,
    Guid? SprintId);

public sealed record TaskCreationRequestResponse(
    Guid Id, Guid ObjectiveId, string Status, TaskCreationRequestPayload Payload, DateTimeOffset CreatedAt);

public sealed record TaskEditRequestResponse(
    Guid Id, Guid TaskId, string Status, TaskEditRequestPayload Payload, string RequestedByName, DateTimeOffset CreatedAt);

/// <summary>Returned alongside a 409 slack-conflict so the frontend can offer the extend-allocation flow (spec §3.2).</summary>
public sealed record InsufficientAllocationResponse(decimal AvailableSlackHours, string SuggestedAction = "extend_allocation");

public static class InsufficientAllocationResponseJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    public static string Serialize(InsufficientAllocationResponse response)
        => System.Text.Json.JsonSerializer.Serialize(response, Options);
}
