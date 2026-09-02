namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record TaskProgressResponse(
    int Completed,
    int InProgress,
    int NotStarted,
    int Overdue,
    int Total);
