using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

/// <summary>Returns a valid (non-expired) access token for an external calendar connection,
/// refreshing and persisting a new one if needed. Returns null and marks the connection
/// ReauthRequired if the refresh itself fails - callers must treat null as "cannot proceed",
/// not retry immediately.</summary>
public interface ICalendarConnectionTokenProvider
{
    Task<string?> GetFreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct);
}
