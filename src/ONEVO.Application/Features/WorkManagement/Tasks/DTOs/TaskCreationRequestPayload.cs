namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

public sealed record TaskCreationRequestPayload(
    string Title, string? Description, Guid CategoryId, string Priority,
    DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints, Guid? SprintId);
