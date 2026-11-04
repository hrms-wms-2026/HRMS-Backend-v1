using System.Text.Json;

namespace ONEVO.Application.Features.DevPlatform.Audit.DTOs.Responses;

public sealed record PlatformAuditLogEntryResponse(
    Guid Id,
    Guid? ActorPlatformUserId,
    string? ActorEmail,
    string Action,
    string TargetType,
    string? TargetId,
    Guid? TargetTenantId,
    JsonElement? Before,
    JsonElement? After,
    string? IpAddress,
    string? UserAgent,
    string? CorrelationId,
    DateTimeOffset CreatedAt);

public sealed record PlatformAuditLogListResponse(
    IReadOnlyList<PlatformAuditLogEntryResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);
