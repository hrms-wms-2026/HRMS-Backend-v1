using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;

/// <summary>
/// Maps PlatformOAuthApp entities to safe response DTOs.
/// SECURITY: ClientSecretEncrypted / PrivateKeyEncrypted are deliberately never mapped;
/// the active credential contributes only presence booleans and its version number.
/// </summary>
public static class PlatformOAuthAppMapper
{
    public static PlatformOAuthAppDto ToDto(
        PlatformOAuthApp app,
        PlatformOAuthAppCredential? activeCredential) => new()
    {
        Id = app.Id,
        Provider = app.Provider,
        AppName = app.AppName,
        LogoUrl = app.LogoUrl,
        ClientId = app.ClientId,
        AuthorizationUrl = app.AuthorizationUrl,
        TokenUrl = app.TokenUrl,
        DefaultScopes = app.DefaultScopes,
        IsActive = app.IsActive,
        LastVerifiedAt = app.LastVerifiedAt,
        UpdatedAt = app.UpdatedAt,
        HasActiveCredential = activeCredential is not null,
        ActiveCredentialVersion = activeCredential?.CredentialVersion,
        HasPrivateKey = !string.IsNullOrEmpty(activeCredential?.PrivateKeyEncrypted)
    };
}
