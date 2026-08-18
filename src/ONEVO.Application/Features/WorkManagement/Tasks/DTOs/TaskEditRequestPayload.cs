namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs;

public sealed record TaskEditRequestPayload(
    string Title, string? Description, string Priority, DateOnly? DueDate, decimal? EstimatedHours, int? StoryPoints);
