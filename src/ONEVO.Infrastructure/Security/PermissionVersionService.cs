using System.Collections.Concurrent;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

namespace ONEVO.Infrastructure.Security;

/// <summary>
/// In-memory permission version counter. Replace with Redis in production.
/// Tracks whether a user's permissions have changed since their session was issued.
/// </summary>
public class PermissionVersionService : IPermissionVersionService
{
    private static readonly ConcurrentDictionary<Guid, long> _versions = new();
    private readonly ISessionTicketCache _ticketCache;

    public PermissionVersionService(ISessionTicketCache ticketCache)
    {
        _ticketCache = ticketCache;
    }

    public Task<long> GetCurrentVersionAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(_versions.GetOrAdd(userId, 0));

    public Task<long> IncrementVersionAsync(Guid userId, CancellationToken ct = default)
    {
        var newVersion = _versions.AddOrUpdate(userId, 1, (_, current) => current + 1);
        // Improvement task 03: a permission version bump is the signal that this user's cached
        // cookie tickets are stale.
        _ticketCache.EvictUser(userId);
        return Task.FromResult(newVersion);
    }
}
