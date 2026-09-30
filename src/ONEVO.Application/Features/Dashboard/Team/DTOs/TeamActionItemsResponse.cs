namespace ONEVO.Application.Features.Dashboard.Team.DTOs;

/// <summary>Approvals &amp; Exceptions widget response (My Team spec §9.4).</summary>
public sealed record TeamActionItemsResponse(Guid LegalEntityId, IReadOnlyList<ActionSourceSummary> Sources);

public sealed record ActionSourceSummary(
    string SourceKey,
    string Domain,
    string Status,
    int PendingCount,
    int? InProgressCount,
    DateTimeOffset? OldestPendingAt,
    IReadOnlyList<ActionItem> TopItems)
{
    public const string StatusOk = "ok";
    public const string StatusUnavailable = "unavailable";
    public const string DomainPeople = "people";
    public const string DomainWork = "work";

    /// <summary>The composition handler's fallback row when a source throws (spec §9.4 "a thrown
    /// exception is logged with the source key and returns { status: 'unavailable' }; the other
    /// sources still return").</summary>
    public static ActionSourceSummary Unavailable(string sourceKey, string domain) =>
        new(sourceKey, domain, StatusUnavailable, 0, null, null, Array.Empty<ActionItem>());
}

public sealed record ActionItem(
    string SourceKey,
    Guid EntityId,
    string Title,
    Guid? SubjectEmployeeId,
    string? SubjectName,
    DateTimeOffset CreatedAt,
    string? ExceptionStatus,
    ActionItemLink Link);

/// <summary>Data only, never a URL (spec §15) - the frontend resolves Kind+Params to a route.</summary>
public sealed record ActionItemLink(string Kind, IReadOnlyDictionary<string, string> Params)
{
    public const string KindLeaveApproval = "leave.approval";
    public const string KindAttendanceApproval = "attendance.approval";
    public const string KindMonitoringException = "monitoring.exception";
    public const string KindWorkRequest = "work.request";
}
