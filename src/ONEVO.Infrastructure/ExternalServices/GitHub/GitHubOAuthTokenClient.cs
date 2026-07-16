using System.Net.Http.Headers;
using System.Text.Json;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

namespace ONEVO.Infrastructure.ExternalServices.GitHub;

public sealed class GitHubOAuthTokenClient : IGitHubOAuthClient
{
    private readonly HttpClient _httpClient;

    public GitHubOAuthTokenClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GitHubOAuthTokenResult?> ExchangeCodeAsync(
        GitHubOAuthTokenRequest request,
        CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, request.TokenUrl);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = request.ClientId,
            ["client_secret"] = request.ClientSecret,
            ["code"] = request.Code,
            ["redirect_uri"] = request.RedirectUri
        });

        using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await ReadTokenResultAsync(response, ct);
    }

    public async Task<GitHubOAuthTokenResult?> RefreshTokenAsync(
        GitHubOAuthRefreshRequest request,
        CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, request.TokenUrl);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = request.ClientId,
            ["client_secret"] = request.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = request.RefreshToken
        });

        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await ReadTokenResultAsync(response, ct);
    }

    public async Task<GitHubUserProfileResult?> GetCurrentUserAsync(
        string accessToken,
        CancellationToken ct)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.github.com/user");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        message.Headers.UserAgent.ParseAdd("ONEVO/1.0");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: ct);
        var root = document.RootElement;
        var providerUserId = GetIdentifier(root, "id");
        var username = GetString(root, "login");
        if (string.IsNullOrWhiteSpace(providerUserId) || string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        return new GitHubUserProfileResult(
            providerUserId,
            username,
            GetString(root, "email"));
    }

    private static string? GetString(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static async Task<GitHubOAuthTokenResult?> ReadTokenResultAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        var accessToken = GetString(root, "access_token");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        return new GitHubOAuthTokenResult(
            accessToken,
            GetString(root, "refresh_token"),
            GetLong(root, "expires_in"),
            GetString(root, "scope"),
            GetString(root, "token_type"));
    }

    private static long? GetLong(JsonElement root, string property)
    {
        return root.TryGetProperty(property, out var value) && value.TryGetInt64(out var result)
            ? result
            : null;
    }

    private static string? GetIdentifier(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
