using ONEVO.Domain.Features.DevPlatform.Audit.Entities;

namespace ONEVO.Application.Features.DevPlatform.Audit.RepositoryInterfaces;

/// <summary>Append-only: there is intentionally no update or delete method.</summary>
public interface IPlatformAuditLogRepository
{
    Task AddAsync(PlatformAuditLog entry, CancellationToken ct = default);

    Task<(IReadOnlyList<PlatformAuditLog> Items, int Total)> ListAsync(
        PlatformAuditLogFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default);
}

public sealed record PlatformAuditLogFilter(
    Guid? ActorPlatformUserId = null,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    Guid? TargetTenantId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);
