using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity;

public class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
    private readonly ILogger<GoogleIdTokenValidator> _logger;

    public GoogleIdTokenValidator(ILogger<GoogleIdTokenValidator> logger) => _logger = logger;

    public async Task<GoogleIdTokenPayload?> ValidateAsync(
        string idToken,
        string expectedAudience,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(expectedAudience))
            return null;

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [expectedAudience]
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            if (payload is null)
                return null;

            return new GoogleIdTokenPayload(
                Subject: payload.Subject,
                Email: payload.Email ?? string.Empty,
                EmailVerified: payload.EmailVerified,
                Name: payload.Name);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Google ID token validation failed.");
            return null;
        }
    }
}
