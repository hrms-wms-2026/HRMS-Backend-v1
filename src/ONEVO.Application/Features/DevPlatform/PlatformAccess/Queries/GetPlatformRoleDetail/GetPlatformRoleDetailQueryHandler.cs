using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Common.Exceptions;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.GetPlatformRoleDetail;

public class GetPlatformRoleDetailQueryHandler : IRequestHandler<GetPlatformRoleDetailQuery, PlatformRoleDetailResponse>
{
    private readonly IPlatformRoleRepository _roleRepository;
    private readonly IPlatformAccessReadRepository _readRepository;

    public GetPlatformRoleDetailQueryHandler(
        IPlatformRoleRepository roleRepository,
        IPlatformAccessReadRepository readRepository)
    {
        _roleRepository = roleRepository;
        _readRepository = readRepository;
    }

    public async Task<PlatformRoleDetailResponse> Handle(GetPlatformRoleDetailQuery request, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetRoleByIdAsync(request.RoleId, cancellationToken);
        if (role == null)
            throw new NotFoundException($"Platform role {request.RoleId} not found.");

        var permissions = await _readRepository.GetRolePermissionsAsync(new[] { role.Id }, cancellationToken);

        return PlatformAccessMapper.MapDetail(role, permissions.Select(p => p.PermissionCode));
    }
}
