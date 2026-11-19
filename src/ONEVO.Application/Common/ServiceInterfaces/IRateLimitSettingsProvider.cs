using ONEVO.Application.Common.Configuration;

namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Source of the global API rate limits (task 07). The limiter asks for the effective settings on
/// every request, so implementations must answer from memory and never block on I/O: a database
/// or admin-app backed provider keeps its own cache and refreshes it in the background.
/// </summary>
public interface IRateLimitSettingsProvider
{
    /// <summary>
    /// The limits that apply to a request of <paramref name="tenantId"/>, or the platform-wide
    /// limits when it is null (anonymous and admin requests). An implementation can return
    /// per-plan or per-tenant overrides here.
    /// </summary>
    RateLimitSettingsSnapshot GetSettings(Guid? tenantId);
}

/// <summary>
/// Settings plus a version that changes whenever any limit in them changes. The version is part
/// of every limiter partition key, so new limits apply on the next request instead of waiting for
/// limiter instances built from the old ones to go idle. Treat <see cref="Settings"/> as read-only.
/// </summary>
public sealed record RateLimitSettingsSnapshot(long Version, RateLimitSettings Settings);
