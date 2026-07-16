using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformUserSessions;

public class ListPlatformUserSessionsQueryHandler : IRequestHandler<ListPlatformUserSessionsQuery, IReadOnlyList<PlatformUserSessionResponse>>
{
    private readonly IPlatformUserSessionRepository _sessionRepository;

    public ListPlatformUserSessionsQueryHandler(IPlatformUserSessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }

    public async Task<IReadOnlyList<PlatformUserSessionResponse>> Handle(ListPlatformUserSessionsQuery request, CancellationToken cancellationToken)
    {
        var sessions = await _sessionRepository.ListByUserIdAsync(request.UserId, cancellationToken);
        return sessions.Select(PlatformAccessMapper.Map).ToList();
    }
}
