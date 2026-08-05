using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformUsers;

public class ListPlatformUsersQueryHandler : IRequestHandler<ListPlatformUsersQuery, IReadOnlyList<PlatformUserResponse>>
{
    private readonly IPlatformUserRepository _userRepository;

    public ListPlatformUsersQueryHandler(IPlatformUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<PlatformUserResponse>> Handle(ListPlatformUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await _userRepository.ListUsersAsync(cancellationToken);
        if (users.Count == 0)
            return Array.Empty<PlatformUserResponse>();

        var roleNames = await _userRepository.GetFirstRoleNamesByUserIdsAsync(
            users.Select(u => u.Id), cancellationToken);

        return users
            .Select(u => PlatformAccessMapper.Map(u, roleNames.TryGetValue(u.Id, out var role) ? role : string.Empty))
            .ToList();
    }
}
