namespace ONEVO.Application.Features.Monitoring.Exceptions.DTOs.Responses;

/// <summary>
/// Everything a reviewer needs to validate one exception case (flow 09): the day's clock-in/out,
/// and for identity cases what raised it, that day's face checks and check-in scans.
/// </summary>
public record ExceptionEvidenceDto(
    Guid ExceptionId,
    string Type,
    DateOnly WorkDate,
    string Timezone,
    AttendanceEvidenceDto? Attendance,
    IdentityEvidenceDto? Identity,
    IReadOnlyList<FaceCheckEvidenceDto> FaceChecks,
    IReadOnlyList<CheckInScanEvidenceDto> CheckInScans,
    DeviceActivityEvidenceDto? DeviceActivity = null,
    PatternEvidenceDto? Pattern = null,
    bool ActivityHidden = false);

/// <summary>The work day's device activity from the tray's idle/active samples.</summary>
/// <param name="DeviceIds">Every tray device that reported that day - more than one is itself worth a look.</param>
/// <param name="Truncated">The day had more runs than are returned; totals still cover the whole day.</param>
public record DeviceActivityEvidenceDto(
    DateTimeOffset? FirstSeenAt,
    DateTimeOffset? LastSeenAt,
    int ActiveMinutes,
    int IdleMinutes,
    int OfflineMinutes,
    IReadOnlyList<Guid> DeviceIds,
    IReadOnlyList<DeviceActivitySegmentDto> Segments,
    bool Truncated);

/// <param name="State">"active", "idle" or "offline".</param>
public record DeviceActivitySegmentDto(DateTimeOffset Start, DateTimeOffset End, string State);

/// <summary>
/// For the nightly multi-day alerts: the days around the case, the days the rule actually looked
/// at (<see cref="DailyPatternPointDto.InRule"/>), and the same numbers and thresholds the
/// detection job compared.
/// </summary>
/// <param name="MeasuresRecalculated">The case predates stored figures, so they were recomputed now
/// and may differ from what was flagged (e.g. after an attendance correction).</param>
public record PatternEvidenceDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DailyPatternPointDto> Days,
    IReadOnlyList<PatternMeasureDto> Measures,
    bool MeasuresRecalculated = false);

public record DailyPatternPointDto(
    DateOnly Date,
    decimal? ActivityScore,
    int? ActiveMinutes,
    int? IdleMinutes,
    int? WorkedMinutes,
    string? AttendanceStatus,
    bool InRule);

/// <param name="Unit">"score", "minutes", "days" or "points".</param>
public record PatternMeasureDto(string Label, decimal Value, string Unit);

public record AttendanceEvidenceDto(
    DateTimeOffset? ClockInAt,
    DateTimeOffset? ClockOutAt,
    string? Source,
    string Status);

/// <param name="Source">"face_check_override" or "check_in_scan".</param>
/// <param name="PhotoUrl">Short-lived signed URL of the photo that raised the case; null when no
/// photo was kept or it could not be signed. Never the enrolled reference photo.</param>
/// <param name="PhotoUnavailable">A photo was kept but could not be served right now - distinct
/// from "no photo was kept".</param>
public record IdentityEvidenceDto(
    string? Source,
    DateTimeOffset? OccurredAt,
    string? Purpose,
    float? SimilarityScore,
    IReadOnlyList<string> Reasons,
    Guid? DeviceRegistrationId,
    string? PhotoUrl,
    DateTimeOffset? PhotoUrlExpiresAt,
    bool PhotoUnavailable = false);

/// <param name="PhotoUrl">Short-lived signed URL of the photo taken at this check; null when none
/// was kept (passed checks, or checks from before failed photos were kept).</param>
/// <param name="PhotoUnavailable">A photo was kept but could not be served right now.</param>
public record FaceCheckEvidenceDto(
    Guid Id,
    DateTimeOffset At,
    string Purpose,
    string Outcome,
    string? FailureReason,
    float? SimilarityScore,
    string? PhotoUrl = null,
    bool PhotoUnavailable = false);

public record CheckInScanEvidenceDto(
    Guid CheckInId,
    DateTimeOffset At,
    string? ScanStatus,
    float? SimilarityScore,
    Guid DeviceRegistrationId,
    string? DeviceSerialNumber);
