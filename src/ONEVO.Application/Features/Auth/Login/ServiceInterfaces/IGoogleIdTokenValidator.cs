namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

/// <summary>
/// Validates Google ID tokens (JWTs from Google Sign-In). Implemented in
/// Infrastructure using <c>Google.Apis.Auth</c>.
/// </summary>
public interface IGoogleIdTokenValidator
{
    Task<GoogleIdTokenPayload?> ValidateAsync(string idToken, string expectedAudience, CancellationToken ct = default);
}

public sealed record GoogleIdTokenPayload(
    string Subject,
    string Email,
    bool EmailVerified,
    string? Name);
