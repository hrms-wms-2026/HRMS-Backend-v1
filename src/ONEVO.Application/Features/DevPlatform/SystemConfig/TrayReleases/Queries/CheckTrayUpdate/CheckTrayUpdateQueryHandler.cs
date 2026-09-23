using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.CheckTrayUpdate;

public sealed record CheckTrayUpdateQuery(string CurrentVersion, string Channel)
    : IRequest<Result<TrayUpdateCheckDto>>;

public sealed class CheckTrayUpdateQueryHandler
    : IRequestHandler<CheckTrayUpdateQuery, Result<TrayUpdateCheckDto>>
{
    private readonly ITrayAppReleaseRepository _repo;
    public CheckTrayUpdateQueryHandler(ITrayAppReleaseRepository repo) => _repo = repo;

    public async Task<Result<TrayUpdateCheckDto>> Handle(
        CheckTrayUpdateQuery request, CancellationToken cancellationToken)
    {
        if (!TrayReleaseChannels.IsValid(request.Channel))
            return Result<TrayUpdateCheckDto>.Failure("channel must be 'stable' or 'beta'.", 400);
        if (!TrayReleaseVersion.TryParse(request.CurrentVersion, out _))
            return Result<TrayUpdateCheckDto>.Failure("current must be x.y.z (numbers only).", 400);

        var latest = await _repo.GetLatestActiveAsync(request.Channel, cancellationToken);
        if (latest is null || TrayReleaseVersion.Compare(latest.Version, request.CurrentVersion) <= 0)
            return Result<TrayUpdateCheckDto>.Success(new TrayUpdateCheckDto(false, false, null));

        var mandatory = TrayReleaseVersion.IsMandatory(request.CurrentVersion, latest.MinSupportedVersion);
        return Result<TrayUpdateCheckDto>.Success(
            new TrayUpdateCheckDto(true, mandatory, TrayReleaseMapper.ToInfo(latest)));
    }
}
