using System.Text.Json.Serialization;

namespace ONEVO.Application.Features.Users.DTOs;

public record MyProfileDto(
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("employee")] EmployeeProfileDto? Employee,
    [property: JsonPropertyName("devices")] IReadOnlyList<MyDeviceDto> Devices,
    [property: JsonPropertyName("work_location")] WorkLocationDto? WorkLocation,
    [property: JsonPropertyName("face_scan")] FaceScanDto? FaceScan
);

public record EmployeeProfileDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("employee_number")] string EmployeeNumber,
    [property: JsonPropertyName("first_name")] string FirstName,
    [property: JsonPropertyName("last_name")] string LastName,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("hire_date")] DateOnly HireDate,
    [property: JsonPropertyName("employment_status_id")] int EmploymentStatusId,
    [property: JsonPropertyName("work_mode_id")] int WorkModeId
);

public record MyDeviceDto(
    [property: JsonPropertyName("agent_id")] Guid AgentId,
    [property: JsonPropertyName("device_name")] string DeviceName,
    [property: JsonPropertyName("os_version")] string OsVersion,
    [property: JsonPropertyName("agent_version")] string AgentVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("last_heartbeat_at")] DateTimeOffset? LastHeartbeatAt
);

public record WorkLocationDto(
    [property: JsonPropertyName("work_mode")] string WorkMode,
    [property: JsonPropertyName("verification_enabled")] bool VerificationEnabled,
    [property: JsonPropertyName("grace_period_minutes")] int? GracePeriodMinutes,
    [property: JsonPropertyName("photo_challenge_on_mismatch")] bool PhotoChallengeOnMismatch
);

public record FaceScanDto(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("captured_at")] DateTimeOffset CapturedAt,
    [property: JsonPropertyName("is_active")] bool IsActive
);
