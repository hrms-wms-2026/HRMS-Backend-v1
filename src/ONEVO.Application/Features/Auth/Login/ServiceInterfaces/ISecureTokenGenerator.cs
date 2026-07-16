namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

public interface ISecureTokenGenerator
{
    /// <summary>Generates a cryptographically random opaque refresh token (raw bytes, base64-encoded).</summary>
    string GenerateOpaqueToken();

    /// <summary>Generates a cryptographically random opaque CSRF token (raw bytes, base64-encoded).
    /// The raw value is set in the readable onevo_csrf cookie; only HashToken(raw) is persisted
    /// server-side on the session.</summary>
    string GenerateCsrfToken();

    /// <summary>SHA-256 hash of a raw token string, for safe storage.</summary>
    string HashToken(string rawToken);
}
