using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Mappers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Queries.ListTrayReleases;

public sealed record ListTrayReleasesQuery() : IRequest<Result<IReadOnlyList<TrayReleaseDto>>>;

public sealed class ListTrayReleasesQueryHandler
    : IRequestHandler<ListTrayReleasesQuery, Result<IReadOnlyList<TrayReleaseDto>>>
{
    private readonly ITrayAppReleaseRepository _repo;
    public ListTrayReleasesQueryHandler(ITrayAppReleaseRepository repo) => _repo = repo;

    public async Task<Result<IReadOnlyList<TrayReleaseDto>>> Handle(
        ListTrayReleasesQuery request, CancellationToken cancellationToken)
    {
        var rows = await _repo.ListAllAsync(cancellationToken);
        return Result<IReadOnlyList<TrayReleaseDto>>.Success(rows.Select(TrayReleaseMapper.ToDto).ToList());
    }
}
