using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Queries.GetPlatformOAuthApp;

/// <summary>
/// Detail lookup by provider slug (case-insensitive). Rejects unsupported providers
/// (unknown, or Phase 2 such as slack) before any repository read. A metadata-only
/// (unconfigured) provider still returns a card; secrets are never included.
/// </summary>
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

        if (!PlatformOAuthProviderCatalog.TryGet(provider, out var definition))
            return Result<PlatformOAuthAppDto>.Failure(
                $"Provider '{provider}' is not an approved OAuth provider.", 400);

        var app = await _repo.GetByProviderAsync(provider, cancellationToken);

        var activeCredential = app is null
            ? null
            : (await _repo.GetActiveCredentialsForAppAsync(app.Id, cancellationToken)).FirstOrDefault();

        return Result<PlatformOAuthAppDto>.Success(
            PlatformOAuthAppMapper.ToDto(definition, app, activeCredential));
    }
}
