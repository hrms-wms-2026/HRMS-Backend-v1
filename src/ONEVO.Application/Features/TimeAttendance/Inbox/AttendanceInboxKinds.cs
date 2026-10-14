using System.Text.Json;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.TimeAttendance.Inbox;

/// <summary>How attendance requests are addressed in the Inbox: ("attendance", kind, requestId).</summary>
public static class AttendanceInboxKinds
{
    public const string Source = "attendance";
    public const string Correction = "correction";
    public const string WorkModeChange = "work_mode_change";
    public const string LocationChange = "location_change";
    public const string DeviceChange = "device_change";
    /// <summary>An attendance alert (plan E1/E2): reviewed by the attendance approver, resolved with an outcome.</summary>
    public const string Alert = "alert";
    public const string ApprovePermission = "attendance:approve";
    public const string ReadPermission = "attendance:read";

    private const InboxCapabilities RequestBase = InboxCapabilities.Chat | InboxCapabilities.Revert | InboxCapabilities.Bulk;

    public static readonly IReadOnlyList<InboxKindDescriptor> Descriptors =
    [
        new(Source, Correction, "Attendance correction", InboxCategories.Request,
            RequestBase | InboxCapabilities.EditBeforeApprove | InboxCapabilities.Cancel, "attendance-correction"),
        new(Source, WorkModeChange, "Work mode change", InboxCategories.Request,
            RequestBase | InboxCapabilities.EditBeforeApprove | InboxCapabilities.Cancel, "work-mode-change"),
        new(Source, LocationChange, "Work location change", InboxCategories.Request,
            RequestBase | InboxCapabilities.Cancel, "location-change"),
        // No cancel: there is no cancel command for device changes.
        new(Source, DeviceChange, "Device change", InboxCategories.Request, RequestBase, "device-change"),
        new(Source, Alert, "Attendance alert", InboxCategories.Alert,
            InboxCapabilities.Chat | InboxCapabilities.Revert | InboxCapabilities.Bulk | InboxCapabilities.Resolve, "attendance-alert"),
    ];

    public static readonly IReadOnlyDictionary<string, EmployeeAuthorityPurpose> Purposes = new Dictionary<string, EmployeeAuthorityPurpose>
    {
        [Correction] = EmployeeAuthorityPurpose.AttendanceCorrectionApproval,
        [WorkModeChange] = EmployeeAuthorityPurpose.WorkAreaChangeApproval,
        [LocationChange] = EmployeeAuthorityPurpose.LocationChangeApproval,
        [DeviceChange] = EmployeeAuthorityPurpose.DeviceChangeApproval,
        // Alerts are reviewed by the attendance approver - the same route as corrections.
        [Alert] = EmployeeAuthorityPurpose.AttendanceCorrectionApproval,
    };

    public static string Label(string kind) => Descriptors.FirstOrDefault(d => d.Kind == kind)?.Label ?? "Attendance request";
}

public static class AttendanceInboxActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Cancel = "cancel";
    public const string Resolve = "resolve";
}

public sealed record AttendanceBreakSnapshot(DateTimeOffset BreakStart, DateTimeOffset? BreakEnd, string? BreakType);

/// <summary>The attendance day as it was before a correction was applied (null = there was no record).</summary>
public sealed record AttendanceRecordSnapshot(
    DateTimeOffset? ActualStart, DateTimeOffset? ActualEnd, int BreakMinutes, int WorkedMinutes, int? LateMinutes, string Status,
    IReadOnlyList<AttendanceBreakSnapshot> Breaks);

public sealed record WorkLocationSnapshot(double Latitude, double Longitude, double? AccuracyMeters, DateTimeOffset UpdatedAt);

/// <summary>What an attendance decision changed, kept in the inbox journal (before/after) so it can be undone.</summary>
public sealed record AttendanceDecisionSnapshot(
    string Title, string Status,
    AttendanceRecordSnapshot? Record = null,
    Guid? WorkModeId = null, string? WorkModeName = null, Guid? RecordWorkModeId = null, string? RecordWorkModeName = null,
    WorkLocationSnapshot? WorkLocation = null,
    Guid? OldDeviceRegistrationId = null, bool? OldDeviceActive = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static AttendanceDecisionSnapshot? Parse(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AttendanceDecisionSnapshot>(json, Json);
}

/// <summary>The resolution outcomes a reviewer picks for an attendance alert (convert_to_leave = absences only).</summary>
public static class AttendanceAlertResolveOutcomes
{
    public const string Excused = "excused";
    public const string Confirmed = "confirmed";
    public const string ConvertToLeave = "convert_to_leave";

    public static bool IsValid(string? outcome) => outcome is Excused or Confirmed or ConvertToLeave;
}

/// <summary>An attendance alert's review state, kept in the inbox journal (before/after) so a resolution can be
/// undone. <see cref="LeaveUpdatedAt"/> is the converted leave's last change when the decision was taken.</summary>
public sealed record AttendanceAlertSnapshot(
    string Title, string Status, string? Outcome, string? Note, Guid? ResolvedByEmployeeId, DateTimeOffset? ResolvedAt,
    Guid? ConvertedLeaveRequestId, DateTimeOffset? LeaveUpdatedAt = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static AttendanceAlertSnapshot? Parse(string? json)
        => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AttendanceAlertSnapshot>(json, Json);
}
