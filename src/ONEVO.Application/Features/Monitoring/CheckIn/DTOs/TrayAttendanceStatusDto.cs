namespace ONEVO.Application.Features.Monitoring.CheckIn.DTOs;

using System.Text.Json.Serialization;

public sealed record TrayAttendanceStatusDto(
    [property: JsonPropertyName("is_clocked_in")] bool IsClockedIn,
    [property: JsonPropertyName("clocked_in_at_utc")] DateTimeOffset? ClockedInAtUtc);
