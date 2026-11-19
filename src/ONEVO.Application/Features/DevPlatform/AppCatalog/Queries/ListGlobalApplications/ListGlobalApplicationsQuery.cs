using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.AppCatalog.DTOs;
using ONEVO.Application.Features.DevPlatform.AppCatalog.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.AppCatalog.Queries.ListGlobalApplications;

public sealed record ListGlobalApplicationsQuery : IRequest<Result<IReadOnlyList<GlobalApplicationDto>>>;

public sealed class ListGlobalApplicationsQueryHandler(IGlobalApplicationRepository applications)
    : IRequestHandler<ListGlobalApplicationsQuery, Result<IReadOnlyList<GlobalApplicationDto>>>
{
    public async Task<Result<IReadOnlyList<GlobalApplicationDto>>> Handle(
        ListGlobalApplicationsQuery request,
        CancellationToken ct)
    {
        var rows = await applications.ListAsync(ct);
        return Result<IReadOnlyList<GlobalApplicationDto>>.Success(rows.Select(GlobalApplicationDto.FromEntity).ToList());
    }
}
