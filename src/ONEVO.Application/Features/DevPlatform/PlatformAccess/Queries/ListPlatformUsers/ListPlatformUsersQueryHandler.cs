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
        return users.Select(PlatformAccessMapper.Map).ToList();
    }
}
