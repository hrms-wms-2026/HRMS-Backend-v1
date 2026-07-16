using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformAuthEvents;

public class ListPlatformAuthEventsQueryHandler : IRequestHandler<ListPlatformAuthEventsQuery, IReadOnlyList<PlatformAuthEventResponse>>
{
    private readonly IPlatformAuthEventRepository _authEventRepository;

    public ListPlatformAuthEventsQueryHandler(IPlatformAuthEventRepository authEventRepository)
    {
        _authEventRepository = authEventRepository;
    }

    public async Task<IReadOnlyList<PlatformAuthEventResponse>> Handle(ListPlatformAuthEventsQuery request, CancellationToken cancellationToken)
    {
        var events = await _authEventRepository.ListAllAsync(cancellationToken);
        return events.Select(PlatformAccessMapper.Map).ToList();
    }
}
