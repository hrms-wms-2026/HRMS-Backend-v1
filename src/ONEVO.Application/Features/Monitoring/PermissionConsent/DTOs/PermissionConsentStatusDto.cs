using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.PermissionConsent.DTOs;

/// <param name="Accepted">The employee has accepted at least once; the flags are from the latest acceptance.</param>
public record PermissionConsentStatusDto(
    [property: JsonPropertyName("accepted")] bool Accepted,
    [property: JsonPropertyName("accepted_at")] DateTimeOffset? AcceptedAt,
    [property: JsonPropertyName("screen_monitoring")] bool ScreenMonitoring,
    [property: JsonPropertyName("app_tracking")] bool AppTracking,
    [property: JsonPropertyName("location_access")] bool LocationAccess,
    [property: JsonPropertyName("camera_access")] bool CameraAccess,
    [property: JsonPropertyName("notifications")] bool Notifications,
    [property: JsonPropertyName("keyboard_mouse")] bool KeyboardMouse)
{
    public static PermissionConsentStatusDto None { get; } =
        new(false, null, false, false, false, false, false, false);
}
