using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Queries.ListPlatformOAuthApps;

/// <summary>Lists all OAuth app registrations. Secrets are never included.</summary>
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

        var activeByAppId = new Dictionary<Guid, PlatformOAuthAppCredential>();
        foreach (var credential in activeCredentials)
            activeByAppId[credential.PlatformOAuthAppId] = credential;

        var dtos = new List<PlatformOAuthAppDto>(apps.Count);
        foreach (var app in apps)
        {
            activeByAppId.TryGetValue(app.Id, out var active);
            dtos.Add(PlatformOAuthAppMapper.ToDto(app, active));
        }

        return Result<IReadOnlyList<PlatformOAuthAppDto>>.Success(dtos);
    }
}
