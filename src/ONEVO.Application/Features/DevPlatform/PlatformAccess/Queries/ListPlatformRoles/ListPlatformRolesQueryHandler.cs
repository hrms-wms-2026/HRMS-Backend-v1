using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformRoles;

public class ListPlatformRolesQueryHandler : IRequestHandler<ListPlatformRolesQuery, IReadOnlyList<PlatformRoleResponse>>
{
    private readonly IPlatformRoleRepository _roleRepository;

    public ListPlatformRolesQueryHandler(IPlatformRoleRepository roleRepository)
    {
        _roleRepository = roleRepository;
    }

    public async Task<IReadOnlyList<PlatformRoleResponse>> Handle(ListPlatformRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await _roleRepository.ListRolesAsync(cancellationToken);
        return roles.Select(PlatformAccessMapper.Map).ToList();
    }
}
