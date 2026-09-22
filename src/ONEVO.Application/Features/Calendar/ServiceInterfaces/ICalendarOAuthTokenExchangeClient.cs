namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record CalendarProviderTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);
public sealed record CalendarProviderAccount(string AccountEmail, string? PrimaryCalendarId, string? PrimaryCalendarName);

public interface ICalendarOAuthTokenExchangeClient
{
    Task<CalendarProviderTokens> ExchangeCodeAsync(string tokenUrl, string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct);
    Task<CalendarProviderTokens> RefreshTokenAsync(string tokenUrl, string clientId, string clientSecret, string refreshToken, CancellationToken ct);

    /// <summary>provider is "google" | "microsoft" (PlatformOAuthProviderCatalog vocabulary, not
    /// CalendarExternalSources) — matches what StartCalendarConnectionCommand/the callback route
    /// already carry, so callers never have to remap.</summary>
    Task<CalendarProviderAccount> GetAccountAsync(string provider, string accessToken, CancellationToken ct);
}
