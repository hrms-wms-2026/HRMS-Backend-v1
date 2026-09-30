namespace ONEVO.Application.Features.WorkManagement.EmployeeOverview.DTOs;

/// <summary>One task row on the Work & Activity Needs attention / Recent tasks cards.</summary>
public sealed record EmployeeWorkTaskItem(
    Guid TaskId,
    string ShortId,
    string Title,
    Guid ProjectId,
    string ProjectName,
    string StatusName,
    string StatusColor,
    string Priority,
    int? StoryPoints,
    DateOnly? DueDate,
    int ProgressPercent);

/// <summary>reason: "overdue" | "due_soon". OverdueDays is set only for overdue items.</summary>
public sealed record EmployeeAttentionItem(
    Guid TaskId,
    string ShortId,
    string Title,
    Guid ProjectId,
    string ProjectName,
    string StatusName,
    string StatusColor,
    int? StoryPoints,
    DateOnly DueDate,
    string Reason,
    int? OverdueDays,
    int? DaysUntilDue);

public sealed record EmployeeNeedsAttentionResponse(DateOnly AsOf, int TotalCount, IReadOnlyList<EmployeeAttentionItem> Items);

public sealed record EmployeeRecentTasksResponse(IReadOnlyList<EmployeeWorkTaskItem> Items);

/// <summary>Month is "YYYY-MM"; Completed counts tasks whose CompletedAt falls in that UTC month.</summary>
public sealed record EmployeeDeliveryTrendMonth(string Month, int Completed);

public sealed record EmployeeDeliveryTrendResponse(IReadOnlyList<EmployeeDeliveryTrendMonth> Months);
