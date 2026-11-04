using ONEVO.Application.Features.WorkManagement.Leadership.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Leadership.Services;

/// <summary>Wire values of the Team Progress detail endpoints (TEAM_PROGRESS_WIDGET_CONTRACT.md).
/// Matching is case-insensitive; blank means "use the default". The validators reject anything
/// else with 400, so the parsers below only ever see known or blank values.</summary>
public static class LedProgressQueryOptions
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 200;

    public static readonly IReadOnlyDictionary<string, LedEmployeeHoursSort> EmployeeSorts =
        new Dictionary<string, LedEmployeeHoursSort>(StringComparer.OrdinalIgnoreCase)
        {
            ["totalHours"] = LedEmployeeHoursSort.TotalHours,
            ["completedHours"] = LedEmployeeHoursSort.CompletedHours,
            ["inProgressHours"] = LedEmployeeHoursSort.InProgressHours,
            ["notStartedHours"] = LedEmployeeHoursSort.NotStartedHours,
            ["name"] = LedEmployeeHoursSort.Name,
        };

    public static readonly IReadOnlyDictionary<string, LedProgressTaskSort> TaskSorts =
        new Dictionary<string, LedProgressTaskSort>(StringComparer.OrdinalIgnoreCase)
        {
            ["estimatedHours"] = LedProgressTaskSort.EstimatedHours,
            ["dueDate"] = LedProgressTaskSort.DueDate,
            ["title"] = LedProgressTaskSort.Title,
            ["status"] = LedProgressTaskSort.Status,
        };

    public static readonly IReadOnlyDictionary<string, LedTaskBucket> Statuses =
        new Dictionary<string, LedTaskBucket>(StringComparer.OrdinalIgnoreCase)
        {
            ["completed"] = LedTaskBucket.Completed,
            ["inProgress"] = LedTaskBucket.InProgress,
            ["notStarted"] = LedTaskBucket.NotStarted,
        };

    public static bool IsBlankOr<T>(string? value, IReadOnlyDictionary<string, T> allowed)
        => string.IsNullOrWhiteSpace(value) || allowed.ContainsKey(value.Trim());

    public static bool IsDirection(string? value)
        => string.IsNullOrWhiteSpace(value)
            || string.Equals(value.Trim(), "asc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "desc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Default "desc"; only an explicit "asc" sorts ascending.</summary>
    public static bool IsDescending(string? direction)
        => !string.Equals(direction?.Trim(), "asc", StringComparison.OrdinalIgnoreCase);

    public static T Parse<T>(string? value, IReadOnlyDictionary<string, T> allowed, T fallback)
        => !string.IsNullOrWhiteSpace(value) && allowed.TryGetValue(value.Trim(), out var parsed) ? parsed : fallback;

    public static string WireStatus(LedTaskBucket bucket) => bucket switch
    {
        LedTaskBucket.Completed => "completed",
        LedTaskBucket.InProgress => "inProgress",
        _ => "notStarted",
    };

    public static (int PageNumber, int PageSize) ClampPaging(int pageNumber, int pageSize)
        => (Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, MaxPageSize));

    public static string? NormalizeSearch(string? search)
        => string.IsNullOrWhiteSpace(search) ? null : search.Trim();

    /// <summary>Skip for a page, guarded against int overflow on absurd page numbers.</summary>
    public static int Skip(int pageNumber, int pageSize)
        => (int)Math.Min(int.MaxValue, ((long)pageNumber - 1) * pageSize);
}
