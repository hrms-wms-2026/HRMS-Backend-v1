namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;

public sealed record GitHubOAuthState(
    string Nonce,
    Guid TenantId,
    Guid UserId,
    string IntegrationKey,
    string Provider,
    string? ReturnUrl,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? SessionBinding);

public interface IOAuthStateProtector
{
    string Protect(GitHubOAuthState state);
    bool TryUnprotect(string protectedState, out GitHubOAuthState? state);
}

public sealed record GitHubOAuthTokenRequest(
    string TokenUrl,
    string ClientId,
    string ClientSecret,
    string Code,
    string RedirectUri);

public sealed record GitHubOAuthTokenResult(
    string AccessToken,
    string? RefreshToken,
    long? ExpiresInSeconds,
    string? Scope,
    string? TokenType);

public sealed record GitHubOAuthRefreshRequest(
    string TokenUrl,
    string ClientId,
    string ClientSecret,
    string RefreshToken);

public sealed record GitHubUserProfileResult(
    string ProviderUserId,
    string Username,
    string? Email);

public interface IGitHubOAuthClient
{
    Task<GitHubOAuthTokenResult?> ExchangeCodeAsync(
        GitHubOAuthTokenRequest request,
        CancellationToken ct);

    Task<GitHubOAuthTokenResult?> RefreshTokenAsync(
        GitHubOAuthRefreshRequest request,
        CancellationToken ct);

    Task<GitHubUserProfileResult?> GetCurrentUserAsync(
        string accessToken,
        CancellationToken ct);
}
