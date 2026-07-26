using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Queries.ListPlatformOAuthApps;

/// <summary>
/// Lists a provider card for every approved OAuth provider (github, google, microsoft,
/// zoom), merging backend-owned catalog metadata with whatever database configuration
/// exists. A provider with no platform_oauth_apps row still appears, as an unconfigured
/// card, so the UI can render it before any operator configuration happens.
/// Secrets are never included.
/// </summary>
public sealed record ListPlatformOAuthAppsQuery : IRequest<Result<IReadOnlyList<PlatformOAuthAppDto>>>;

public sealed class ListPlatformOAuthAppsQueryHandler
    : IRequestHandler<ListPlatformOAuthAppsQuery, Result<IReadOnlyList<PlatformOAuthAppDto>>>
{
    private readonly IPlatformOAuthAppRepository _repo;

    public ListPlatformOAuthAppsQueryHandler(IPlatformOAuthAppRepository repo)
        => _repo = repo;

    public async Task<Result<IReadOnlyList<PlatformOAuthAppDto>>> Handle(
        ListPlatformOAuthAppsQuery request,
        CancellationToken cancellationToken)
    {
        var apps = await _repo.ListAllAsync(cancellationToken);
        var activeCredentials = await _repo.ListActiveCredentialsAsync(cancellationToken);

        var appsByProvider = new Dictionary<string, PlatformOAuthApp>(StringComparer.Ordinal);
        foreach (var app in apps)
            appsByProvider[app.Provider] = app;

        var activeByAppId = new Dictionary<Guid, PlatformOAuthAppCredential>();
        foreach (var credential in activeCredentials)
            activeByAppId[credential.PlatformOAuthAppId] = credential;

        var dtos = new List<PlatformOAuthAppDto>();
        foreach (var definition in PlatformOAuthProviderCatalog.GetAll())
        {
            appsByProvider.TryGetValue(definition.Provider, out var app);
            PlatformOAuthAppCredential? active = null;
            if (app is not null)
                activeByAppId.TryGetValue(app.Id, out active);

            dtos.Add(PlatformOAuthAppMapper.ToDto(definition, app, active));
        }

        return Result<IReadOnlyList<PlatformOAuthAppDto>>.Success(dtos);
    }
}
