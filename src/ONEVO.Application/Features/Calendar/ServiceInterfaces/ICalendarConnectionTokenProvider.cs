using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

/// <summary>Returns a valid (non-expired) access token for an external calendar connection,
/// refreshing and persisting a new one if needed. Returns null and marks the connection
/// ReauthRequired if the refresh itself fails - callers must treat null as "cannot proceed",
/// not retry immediately.</summary>
public interface ICalendarConnectionTokenProvider
{
    Task<string?> GetFreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct);

    /// <summary>Refreshes even if the stored token looks valid - for a provider that just answered 401.
    /// Null (and the connection marked re-auth) when the refresh fails.</summary>
    Task<string?> ForceRefreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct);
}
