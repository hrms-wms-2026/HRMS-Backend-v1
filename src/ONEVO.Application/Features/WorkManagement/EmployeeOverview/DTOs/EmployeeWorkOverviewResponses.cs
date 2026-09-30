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
