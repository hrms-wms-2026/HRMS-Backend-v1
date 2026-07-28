using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;

/// <summary>
/// Maps a catalog provider definition, plus whatever operator configuration exists,
/// to a safe response DTO. Works even when no platform_oauth_apps row exists yet, so
/// callers can render every approved provider as a card.
/// SECURITY: ClientSecretEncrypted / PrivateKeyEncrypted are deliberately never mapped;
/// the active credential contributes only presence booleans and its version number.
/// </summary>
public static class PlatformOAuthAppMapper
{
    public static PlatformOAuthAppDto ToDto(
        PlatformOAuthProviderDefinition definition,
        PlatformOAuthApp? app,
        PlatformOAuthAppCredential? activeCredential)
    {
        var hasClientId = app is not null && !string.IsNullOrWhiteSpace(app.ClientId);
        var hasRequiredCredential = !definition.ClientSecretRequired || activeCredential is not null;
        var configured = app is not null && hasClientId && hasRequiredCredential;

        return new PlatformOAuthAppDto
        {
            Provider = definition.Provider,
            DisplayName = definition.DisplayName,
            AppName = app is not null && !string.IsNullOrWhiteSpace(app.AppName) ? app.AppName : null,
            LogoUrl = app?.LogoUrl,
            Configured = configured,
            IsActive = app?.IsActive ?? false,
            ClientId = hasClientId ? app!.ClientId : null,
            AuthorizationUrl = definition.AuthorizationUrl,
            TokenUrl = definition.TokenUrl,
            DefaultScopes = definition.DefaultScopes,
            Capabilities = definition.Capabilities,
            ClientSecretRequired = definition.ClientSecretRequired,
            HasActiveCredential = activeCredential is not null,
            ActiveCredentialVersion = activeCredential?.CredentialVersion,
            HasPrivateKey = !string.IsNullOrEmpty(activeCredential?.PrivateKeyEncrypted),
            LastVerifiedAt = app?.LastVerifiedAt,
            UpdatedAt = app?.UpdatedAt
        };
    }
}
