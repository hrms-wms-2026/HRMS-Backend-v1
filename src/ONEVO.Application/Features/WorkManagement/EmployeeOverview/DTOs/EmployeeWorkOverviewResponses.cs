namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

public sealed record EmployeeWorkOverviewResponse(
    DateOnly From,
    DateOnly To,
    int Assigned,
    int Completed,
    int InProgress,
    int Overdue,
    int NotStarted,
    int CompletionRatePercent,
    int? OnTimeRatePercent);

/// <summary>
/// One task on the Work card's task list. Bucket: overdue | not_started | in_progress | completed
/// (the same rules as the card's counts). Priority: critical | high | medium | low. ModuleName is the
/// task's objective title, empty if it cannot be resolved.
/// </summary>
public sealed record EmployeeWorkTaskItem(
    Guid TaskId,
    string ShortId,
    string Title,
    string Bucket,
    string Priority,
    string StatusName,
    string StatusColor,
    DateOnly? DueDate,
    int DaysOverdue,
    bool IsCarriedOver,
    Guid ProjectId,
    string ProjectName,
    Guid ObjectiveId,
    string ModuleName);

/// <summary>Every task behind the Work card for from..to, ordered overdue, not started, in progress,
/// completed; then by priority (critical first); then by due date (none last).</summary>
public sealed record EmployeeWorkTasksResponse(DateOnly From, DateOnly To, IReadOnlyList<EmployeeWorkTaskItem> Items);

public sealed record EmployeeDeliveryMetrics(
    int TasksAssigned,
    int TasksCompleted,
    int OnTimeCompleted,
    int CompletedWithDueDate,
    int StoryPointsAssigned,
    int StoryPointsCompleted);

/// <summary>Flat current-period metrics plus, when compare=previous, the same metrics for the
/// previous period.</summary>
public sealed record EmployeeDeliveryResponse(
    DateOnly From,
    DateOnly To,
    int TasksAssigned,
    int TasksCompleted,
    int OnTimeCompleted,
    int CompletedWithDueDate,
    int StoryPointsAssigned,
    int StoryPointsCompleted,
    EmployeeDeliveryMetrics? Previous);
