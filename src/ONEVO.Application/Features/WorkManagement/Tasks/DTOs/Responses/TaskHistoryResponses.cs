namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public static class TaskHistoryEntryTypes
{
    public const string Edit = "edit";
    public const string StatusChange = "status_change";
    public const string ClockSession = "clock_session";
    public const string PercentageChange = "percentage_change";
}

public sealed record TaskHistoryEntryResponse(
    string Type, DateTimeOffset OccurredAt, Guid EmployeeId, string EmployeeName,
    TaskEditEntryDetails? Edit, TaskStatusChangeEntryDetails? StatusChange,
    TaskClockSessionEntryDetails? ClockSession, TaskPercentageChangeEntryDetails? PercentageChange);

public sealed record TaskEditEntryDetails(
    Guid LogId, string Source, Guid? EditRequestId, string OldValuesJson, string NewValuesJson, string? Reason);

public sealed record TaskStatusChangeEntryDetails(Guid FromStatusId, Guid ToStatusId);

public sealed record TaskClockSessionEntryDetails(
    Guid SessionId, DateTimeOffset ClockInAt, DateTimeOffset? ClockOutAt, int? DurationMinutes,
    string? SessionReason, int? PushedPercent, int? PreviousPercent, Guid? PercentageLogId,
    string? PercentageReason);

public sealed record TaskPercentageChangeEntryDetails(
    Guid LogId, int PreviousPercent, int NewPercent, string Source, string? Reason);

public sealed record TaskHistoryResponse(IReadOnlyList<TaskHistoryEntryResponse> Entries);
