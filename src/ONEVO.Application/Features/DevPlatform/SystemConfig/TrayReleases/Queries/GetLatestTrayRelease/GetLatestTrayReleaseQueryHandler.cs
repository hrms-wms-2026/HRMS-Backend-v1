using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.GetLatestTrayRelease;

public sealed record GetLatestTrayReleaseQuery(string Channel) : IRequest<Result<TrayInstallerInfoDto>>;

public sealed class GetLatestTrayReleaseQueryHandler
    : IRequestHandler<GetLatestTrayReleaseQuery, Result<TrayInstallerInfoDto>>
{
    private readonly ITrayAppReleaseRepository _repo;
    public GetLatestTrayReleaseQueryHandler(ITrayAppReleaseRepository repo) => _repo = repo;

    public async Task<Result<TrayInstallerInfoDto>> Handle(
        GetLatestTrayReleaseQuery request, CancellationToken cancellationToken)
    {
        if (!TrayReleaseChannels.IsValid(request.Channel))
            return Result<TrayInstallerInfoDto>.Failure("channel must be 'stable' or 'beta'.", 400);

        var latest = await _repo.GetLatestActiveAsync(request.Channel, cancellationToken);
        return latest is null
            ? Result<TrayInstallerInfoDto>.NotFound("No tray installer has been published yet.")
            : Result<TrayInstallerInfoDto>.Success(TrayReleaseMapper.ToInfo(latest));
    }
}
