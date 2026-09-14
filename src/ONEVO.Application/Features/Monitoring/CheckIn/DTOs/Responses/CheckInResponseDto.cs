using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

public record CheckInResponseDto(
    [property: JsonPropertyName("check_in_id")] Guid CheckInId,
    [property: JsonPropertyName("checked_in_at")] DateTimeOffset CheckedInAt,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("device_serial_number")] string? DeviceSerialNumber,
    [property: JsonPropertyName("face_scan_required")] bool FaceScanRequired,
    // Set only when there is an approved-but-not-yet-applied LocationChangeRequest for this
    // employee and location monitoring is on for them - the tray shows the "save this as your
    // new location?" prompt and answers it via RespondToLocationChangeRequest with this id.
    [property: JsonPropertyName("pending_location_change_request_id")] Guid? PendingLocationChangeRequestId = null);
