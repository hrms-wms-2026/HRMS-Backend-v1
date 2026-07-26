namespace ONEVO.Application.Features.Auth.Login.DTOs.Responses;

/// <summary>
/// Public, anonymous-safe Google SSO config for the admin login page.
/// SECURITY: never includes clientSecret, encrypted secret, private key, tokenUrl,
/// authorizationUrl, scopes, credential ids/version, or any provider payload.
/// </summary>
public sealed class AdminGoogleSsoConfigDto
{
    public bool Enabled { get; init; }
    public string? ClientId { get; init; }
}
