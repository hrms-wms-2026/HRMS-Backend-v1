namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Rolling count of 429s answered by the API rate limiters on this instance (Improvements task 49),
/// so an admin can see which tenants hit their limits before changing one. Counts live in memory:
/// they are per API server and start again on a restart. Holds only tenant ids and subject kinds,
/// never a user id, IP address or session.
/// </summary>
public interface IRateLimitRejectionStats
{
    /// <summary>Longest window that can be read, in minutes.</summary>
    const int MaxWindowMinutes = 60;

    /// <summary>
    /// Counts one rejection. <paramref name="tenantId"/> is null for requests with no tenant
    /// (signed-out, pre-authentication and platform admin requests). Must not block or throw.
    /// </summary>
    void Record(Guid? tenantId, string kind);

    /// <summary>Rejections in the last <paramref name="minutes"/> (1 to 60), per tenant and kind.</summary>
    RateLimitRejectionSnapshot Read(int minutes);
}

/// <param name="CountingSince">When this instance started counting (its start, in practice).</param>
public sealed record RateLimitRejectionSnapshot(
    DateTimeOffset GeneratedAt,
    DateTimeOffset CountingSince,
    IReadOnlyList<RateLimitRejectionCount> Counts);

public sealed record RateLimitRejectionCount(Guid? TenantId, string Kind, long Count);
