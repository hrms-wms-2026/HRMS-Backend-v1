namespace ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;

/// <summary>
/// 429s answered by this API server in the last <see cref="Minutes"/> minutes (Improvements task 49).
/// <see cref="Scope"/> is always "instance": until task 21 adds shared counters each API server
/// counts on its own, and the counts start again when it restarts.
/// </summary>
public sealed record RateLimitRejectionsDto(
    string Scope,
    string Note,
    int Minutes,
    DateTimeOffset GeneratedAt,
    DateTimeOffset CountingSince,
    long TotalRejections,
    IReadOnlyList<TenantRateLimitRejectionsDto> Tenants);

/// <summary>
/// One tenant's rejections. <see cref="TenantId"/> is null for requests with no tenant
/// (signed-out, pre-authentication and platform admin requests); those rows have no effective limits.
/// </summary>
public sealed record TenantRateLimitRejectionsDto(
    Guid? TenantId,
    string? TenantName,
    long TotalRejections,
    IReadOnlyList<RateLimitRejectionKindDto> ByKind,
    IReadOnlyList<RateLimitRejectionLimitDto> EffectiveLimits);

/// <summary>
/// Kind is the limiter partition: Anonymous, User, TrayDevice, AdminAnonymous, Admin or
/// PreAuthentication. A User or TrayDevice rejection can come from the caller's own limit or from
/// the tenant ceiling; the limiter does not say which.
/// </summary>
public sealed record RateLimitRejectionKindDto(string Kind, long Count);

/// <summary>An effective per-tenant limit (SignedIn, TrayDevice or TenantCeiling) right now.</summary>
public sealed record RateLimitRejectionLimitDto(string LimitName, int PerMinute, int? Burst);
