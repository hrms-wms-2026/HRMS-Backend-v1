using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformAuthEvents;

public class ListPlatformAuthEventsQueryHandler : IRequestHandler<ListPlatformAuthEventsQuery, IReadOnlyList<PlatformAuthEventResponse>>
{
    private readonly IPlatformAuthEventRepository _authEventRepository;
    private readonly IPlatformUserRepository _userRepository;

    public ListPlatformAuthEventsQueryHandler(
        IPlatformAuthEventRepository authEventRepository,
        IPlatformUserRepository userRepository)
    {
        _authEventRepository = authEventRepository;
        _userRepository = userRepository;
    }

    public async Task<IReadOnlyList<PlatformAuthEventResponse>> Handle(
        ListPlatformAuthEventsQuery request,
        CancellationToken cancellationToken)
    {
        var events = await _authEventRepository.ListAllAsync(cancellationToken);
        var users = await _userRepository.ListUsersAsync(cancellationToken);
        var usersById = users.ToDictionary(u => u.Id);

        return events
            .Select(authEvent =>
            {
                PlatformUser? user = null;
                if (authEvent.UserId is Guid userId)
                {
                    usersById.TryGetValue(userId, out user);
                }

                return PlatformAccessMapper.Map(authEvent, user);
            })
            .ToList();
    }
}
