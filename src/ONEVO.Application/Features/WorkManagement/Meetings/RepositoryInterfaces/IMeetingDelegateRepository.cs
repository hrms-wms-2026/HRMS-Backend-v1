using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

/// <summary>
/// Meeting delegates (spec "Model"). A grant is <b>active</b> when it is not revoked, its expiry is null or later than
/// <c>now</c>, and both employees are active (Active or On leave). Never calls SaveChangesAsync.
/// </summary>
public interface IMeetingDelegateRepository
{
    void Add(MeetingDelegate row);

    /// <summary>The unrevoked grant with this id, tracked so it can be revoked. Expired grants are returned too.</summary>
    Task<MeetingDelegate?> GetUnrevokedTrackedAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>The unrevoked grant for this pair, tracked, expired or not.</summary>
    Task<MeetingDelegate?> GetUnrevokedTrackedForPairAsync(Guid tenantId, Guid principalEmployeeId, Guid delegateEmployeeId,
        CancellationToken ct = default);

    /// <summary>Every unrevoked grant this principal made, expired ones included, oldest first, untracked.</summary>
    Task<IReadOnlyList<MeetingDelegate>> ListUnrevokedForPrincipalAsync(Guid tenantId, Guid principalEmployeeId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListActiveDelegateIdsAsync(Guid tenantId, Guid principalEmployeeId, DateTimeOffset now,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ListActivePrincipalIdsAsync(Guid tenantId, Guid delegateEmployeeId, DateTimeOffset now,
        CancellationToken ct = default);

    Task<bool> IsActiveDelegateAsync(Guid tenantId, Guid principalEmployeeId, Guid delegateEmployeeId, DateTimeOffset now,
        CancellationToken ct = default);
}
