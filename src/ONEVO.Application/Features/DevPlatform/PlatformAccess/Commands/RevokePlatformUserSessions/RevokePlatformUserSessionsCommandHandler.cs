using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserSessions;

public class RevokePlatformUserSessionsCommandHandler : IRequestHandler<RevokePlatformUserSessionsCommand>
{
    private readonly IPlatformUserSessionRepository _sessionRepository;
    private readonly ICurrentPlatformUserContext _currentUser;

    public RevokePlatformUserSessionsCommandHandler(
        IPlatformUserSessionRepository sessionRepository,
        ICurrentPlatformUserContext currentUser)
    {
        _sessionRepository = sessionRepository;
        _currentUser = currentUser;
    }

    public async Task Handle(RevokePlatformUserSessionsCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId == null)
            throw new UnauthorizedAccessException("Current platform user cannot be resolved.");

        await _sessionRepository.RevokeAllByUserIdAsync(request.UserId, cancellationToken);
    }
}
