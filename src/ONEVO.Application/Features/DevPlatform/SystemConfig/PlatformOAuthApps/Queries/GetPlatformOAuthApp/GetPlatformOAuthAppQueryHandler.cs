using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Queries.GetPlatformOAuthApp;

/// <summary>Detail lookup by provider slug (case-insensitive). Secrets are never included.</summary>
public sealed record GetPlatformOAuthAppQuery(string Provider) : IRequest<Result<PlatformOAuthAppDto>>;

public sealed class GetPlatformOAuthAppQueryHandler
    : IRequestHandler<GetPlatformOAuthAppQuery, Result<PlatformOAuthAppDto>>
{
    private readonly IPlatformOAuthAppRepository _repo;

    public GetPlatformOAuthAppQueryHandler(IPlatformOAuthAppRepository repo)
        => _repo = repo;

    public async Task<Result<PlatformOAuthAppDto>> Handle(
        GetPlatformOAuthAppQuery request,
        CancellationToken cancellationToken)
    {
        var provider = PlatformOAuthProviderRules.Normalize(request.Provider);

        var app = await _repo.GetByProviderAsync(provider, cancellationToken);
        if (app is null)
            return Result<PlatformOAuthAppDto>.NotFound(
                $"OAuth app for provider '{provider}' was not found.");

        var activeCredentials = await _repo.GetActiveCredentialsForAppAsync(app.Id, cancellationToken);
        return Result<PlatformOAuthAppDto>.Success(
            PlatformOAuthAppMapper.ToDto(app, activeCredentials.FirstOrDefault()));
    }
}
