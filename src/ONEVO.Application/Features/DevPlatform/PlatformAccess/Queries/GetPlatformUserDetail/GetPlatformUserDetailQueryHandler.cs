using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Common.Exceptions;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.GetPlatformUserDetail;

public class GetPlatformUserDetailQueryHandler : IRequestHandler<GetPlatformUserDetailQuery, PlatformUserDetailResponse>
{
    private readonly IPlatformUserRepository _userRepository;
    private readonly IPlatformAccessReadRepository _readRepository;

    public GetPlatformUserDetailQueryHandler(
        IPlatformUserRepository userRepository,
        IPlatformAccessReadRepository readRepository)
    {
        _userRepository = userRepository;
        _readRepository = readRepository;
    }

    public async Task<PlatformUserDetailResponse> Handle(GetPlatformUserDetailQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (user == null)
            throw new NotFoundException($"Platform user {request.UserId} not found.");

        var userRoles = await _readRepository.GetUserRolesAsync(user.Id, cancellationToken);
        var roles = await _readRepository.GetRolesByIdsAsync(userRoles.Select(ur => ur.RoleId).ToList(), cancellationToken);

        return PlatformAccessMapper.MapDetail(user, roles);
    }
}
