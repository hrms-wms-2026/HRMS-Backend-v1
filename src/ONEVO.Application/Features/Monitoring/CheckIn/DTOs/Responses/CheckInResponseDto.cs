using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs.Responses;

public record CheckInResponseDto(
    [property: JsonPropertyName("check_in_id")] Guid CheckInId,
    [property: JsonPropertyName("checked_in_at")] DateTimeOffset CheckedInAt,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("device_serial_number")] string? DeviceSerialNumber,
    [property: JsonPropertyName("face_scan_required")] bool FaceScanRequired);
