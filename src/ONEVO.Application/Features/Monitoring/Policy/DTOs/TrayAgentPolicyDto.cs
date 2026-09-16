using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.Policy.DTOs;

public sealed record TrayAgentPolicyDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("activity_signal_enabled")] bool ActivitySignalEnabled,
    [property: JsonPropertyName("app_usage_enabled")] bool AppUsageEnabled,
    [property: JsonPropertyName("screenshot_enabled")] bool ScreenshotEnabled,
    [property: JsonPropertyName("inactivity_screenshot_enabled")] bool InactivityScreenshotEnabled,
    [property: JsonPropertyName("camera_verification_enabled")] bool CameraVerificationEnabled,
    [property: JsonPropertyName("idle_threshold_minutes")] int IdleThresholdMinutes,
    [property: JsonPropertyName("valid_until")] DateTimeOffset ValidUntil,
    [property: JsonPropertyName("effective_scope")] string EffectiveScope = "employee",
    [property: JsonPropertyName("location_tracking_enabled")] bool LocationTrackingEnabled = false,
    [property: JsonPropertyName("tray_clock_in_enabled")] bool TrayClockInEnabled = false,
    [property: JsonPropertyName("schedule_start")] TimeOnly? ScheduleStart = null,
    [property: JsonPropertyName("schedule_end")] TimeOnly? ScheduleEnd = null,
    [property: JsonPropertyName("biometric_enabled")] bool BiometricEnabled = false,
    [property: JsonPropertyName("web_enabled")] bool WebEnabled = false,
    [property: JsonPropertyName("photo_required_enabled")] bool PhotoRequiredEnabled = false,
    [property: JsonPropertyName("allowed_radius_meters")] int? AllowedRadiusMeters = null,
    [property: JsonPropertyName("allows_daily_location_choice")] bool AllowsDailyLocationChoice = false,
    [property: JsonPropertyName("self_registers_location")] bool SelfRegistersLocation = false,
    [property: JsonPropertyName("office_latitude")] double? OfficeLatitude = null,
    [property: JsonPropertyName("office_longitude")] double? OfficeLongitude = null);
