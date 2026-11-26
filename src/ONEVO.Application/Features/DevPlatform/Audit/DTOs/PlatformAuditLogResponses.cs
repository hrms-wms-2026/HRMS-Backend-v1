namespace ONEVO.Application.Features.DevPlatform.Audit.DTOs;

public sealed record PlatformAuditLogEntryResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid? ActorPlatformUserId,
    string? ActorEmail,
    string? ActorName,
    string Action,
    string TargetType,
    string? TargetId,
    Guid? TargetTenantId,
    string Outcome,
    string? FailureReason,
    string? BeforeJson,
    string? AfterJson,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId);

public sealed record PlatformAuditLogListResponse(
    IReadOnlyList<PlatformAuditLogEntryResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);
