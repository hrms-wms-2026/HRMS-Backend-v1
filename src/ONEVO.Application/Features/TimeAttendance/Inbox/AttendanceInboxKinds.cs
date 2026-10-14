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
    ];

    public static readonly IReadOnlyDictionary<string, EmployeeAuthorityPurpose> Purposes = new Dictionary<string, EmployeeAuthorityPurpose>
    {
        [Correction] = EmployeeAuthorityPurpose.AttendanceCorrectionApproval,
        [WorkModeChange] = EmployeeAuthorityPurpose.WorkAreaChangeApproval,
        [LocationChange] = EmployeeAuthorityPurpose.LocationChangeApproval,
        [DeviceChange] = EmployeeAuthorityPurpose.DeviceChangeApproval,
    };

    public static string Label(string kind) => Descriptors.FirstOrDefault(d => d.Kind == kind)?.Label ?? "Attendance request";
}

public static class AttendanceInboxActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Cancel = "cancel";
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
