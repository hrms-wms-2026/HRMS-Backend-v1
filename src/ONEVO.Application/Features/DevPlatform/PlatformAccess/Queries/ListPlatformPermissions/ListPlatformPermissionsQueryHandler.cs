using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformPermissions;

public class ListPlatformPermissionsQueryHandler : IRequestHandler<ListPlatformPermissionsQuery, IReadOnlyList<PlatformPermissionResponse>>
{
    public Task<IReadOnlyList<PlatformPermissionResponse>> Handle(ListPlatformPermissionsQuery request, CancellationToken cancellationToken)
    {
        var permissions = PlatformPermissionCatalog.GetAll()
            .Select(PlatformAccessMapper.Map)
            .ToList();

        return Task.FromResult<IReadOnlyList<PlatformPermissionResponse>>(permissions);
    }
}
