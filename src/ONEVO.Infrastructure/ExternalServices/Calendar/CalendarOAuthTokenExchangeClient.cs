using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.Calendar;

public sealed class CalendarOAuthTokenExchangeClient(HttpClient httpClient) : ICalendarOAuthTokenExchangeClient
{
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn);

    public async Task<CalendarProviderTokens> ExchangeCodeAsync(string tokenUrl, string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };
        return await PostTokenRequestAsync(tokenUrl, form, ct);
    }

    public async Task<CalendarProviderTokens> RefreshTokenAsync(string tokenUrl, string clientId, string clientSecret, string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        };
        var tokens = await PostTokenRequestAsync(tokenUrl, form, ct);
        // Refresh responses often omit refresh_token (it doesn't rotate) - keep the caller's original.
        return tokens.RefreshToken is null ? tokens with { RefreshToken = refreshToken } : tokens;
    }

    private async Task<CalendarProviderTokens> PostTokenRequestAsync(string tokenUrl, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await httpClient.PostAsync(tokenUrl, new FormUrlEncodedContent(form), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Token endpoint returned an empty response.");
        var expiresAt = body.ExpiresIn.HasValue ? DateTimeOffset.UtcNow.AddSeconds(body.ExpiresIn.Value) : (DateTimeOffset?)null;
        return new CalendarProviderTokens(body.AccessToken, body.RefreshToken, expiresAt);
    }

    public async Task<CalendarProviderAccount> GetAccountAsync(string provider, string accessToken, CancellationToken ct)
    {
        return provider.Equals("google", StringComparison.OrdinalIgnoreCase)
            ? await GetGoogleAccountAsync(accessToken, ct)
            : await GetMicrosoftAccountAsync(accessToken, ct);
    }

    private async Task<CalendarProviderAccount> GetGoogleAccountAsync(string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/calendar/v3/users/me/calendarList");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.TryGetProperty("primary", out var primary) && primary.GetBoolean())
            {
                var id = item.GetProperty("id").GetString()!; // Google's primary calendar id IS the account email.
                var name = item.TryGetProperty("summary", out var summary) ? summary.GetString() : null;
                return new CalendarProviderAccount(id, id, name);
            }
        }
        throw new InvalidOperationException("No primary Google calendar found for this account.");
    }

    private async Task<CalendarProviderAccount> GetMicrosoftAccountAsync(string accessToken, CancellationToken ct)
    {
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var meResponse = await httpClient.SendAsync(meRequest, ct);
        meResponse.EnsureSuccessStatusCode();
        using var meStream = await meResponse.Content.ReadAsStreamAsync(ct);
        using var meDoc = await JsonDocument.ParseAsync(meStream, cancellationToken: ct);
        var email = (meDoc.RootElement.TryGetProperty("mail", out var mail) ? mail.GetString() : null)
            ?? meDoc.RootElement.GetProperty("userPrincipalName").GetString()!;

        using var calRequest = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me/calendars");
        calRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var calResponse = await httpClient.SendAsync(calRequest, ct);
        calResponse.EnsureSuccessStatusCode();
        using var calStream = await calResponse.Content.ReadAsStreamAsync(ct);
        using var calDoc = await JsonDocument.ParseAsync(calStream, cancellationToken: ct);

        foreach (var item in calDoc.RootElement.GetProperty("value").EnumerateArray())
        {
            if (item.TryGetProperty("isDefaultCalendar", out var isDefault) && isDefault.GetBoolean())
            {
                var id = item.GetProperty("id").GetString();
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                return new CalendarProviderAccount(email, id, name);
            }
        }
        return new CalendarProviderAccount(email, null, null);
    }
}
