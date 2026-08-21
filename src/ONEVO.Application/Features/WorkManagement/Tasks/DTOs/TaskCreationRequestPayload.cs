namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

public sealed record TaskCreationRequestPayload(
    string Title, string? Description, string TaskType, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid? SprintId);
