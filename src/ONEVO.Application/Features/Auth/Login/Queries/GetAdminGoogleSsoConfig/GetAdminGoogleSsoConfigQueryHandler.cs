using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.Login.Queries.GetAdminGoogleSsoConfig;

/// <summary>
/// Resolves whether Google SSO can be offered on the admin login page, and the public
/// clientId to use for it. Reads platform_oauth_apps directly (never the credential
/// resolver) so this path can never touch decrypted secret material.
/// enabled=false whenever google is unsupported, missing, inactive, missing clientId,
/// or missing a required active credential - the caller (an anonymous login page)
/// gets no signal beyond that boolean.
/// </summary>
public sealed record GetAdminGoogleSsoConfigQuery : IRequest<Result<AdminGoogleSsoConfigDto>>;

public sealed class GetAdminGoogleSsoConfigQueryHandler
    : IRequestHandler<GetAdminGoogleSsoConfigQuery, Result<AdminGoogleSsoConfigDto>>
{
    private const string GoogleProvider = "google";

    private readonly IPlatformOAuthAppRepository _repo;

    public GetAdminGoogleSsoConfigQueryHandler(IPlatformOAuthAppRepository repo)
        => _repo = repo;

    public async Task<Result<AdminGoogleSsoConfigDto>> Handle(
        GetAdminGoogleSsoConfigQuery request,
        CancellationToken cancellationToken)
    {
        if (!PlatformOAuthProviderCatalog.TryGet(GoogleProvider, out var definition))
            return Result<AdminGoogleSsoConfigDto>.Success(new AdminGoogleSsoConfigDto { Enabled = false, ClientId = null });

        var app = await _repo.GetByProviderAsync(GoogleProvider, cancellationToken);
        if (app is null || !app.IsActive || string.IsNullOrWhiteSpace(app.ClientId))
            return Result<AdminGoogleSsoConfigDto>.Success(new AdminGoogleSsoConfigDto { Enabled = false, ClientId = null });

        if (definition.ClientSecretRequired)
        {
            var activeCredentials = await _repo.GetActiveCredentialsForAppAsync(app.Id, cancellationToken);
            if (activeCredentials.Count == 0)
                return Result<AdminGoogleSsoConfigDto>.Success(new AdminGoogleSsoConfigDto { Enabled = false, ClientId = null });
        }

        return Result<AdminGoogleSsoConfigDto>.Success(new AdminGoogleSsoConfigDto
        {
            Enabled = true,
            ClientId = app.ClientId
        });
    }
}
