namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record ObjectiveDeadlineItem(Guid ObjectiveId, string Title, DateOnly EndDate);
public sealed record TaskDeadlineItem(Guid TaskId, string ShortId, string Title, DateOnly DueDate);
public sealed record MyDeadlinesResponse(
    IReadOnlyList<ObjectiveDeadlineItem> ObjectiveDeadlines,
    IReadOnlyList<TaskDeadlineItem> TaskDeadlines);
