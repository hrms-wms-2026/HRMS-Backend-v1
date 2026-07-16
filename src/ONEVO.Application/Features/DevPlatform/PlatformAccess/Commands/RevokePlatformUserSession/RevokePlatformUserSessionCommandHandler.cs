using MediatR;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Common.Exceptions;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserSession;

public class RevokePlatformUserSessionCommandHandler : IRequestHandler<RevokePlatformUserSessionCommand>
{
    private readonly IPlatformUserSessionRepository _sessionRepository;
    private readonly ICurrentPlatformUserContext _currentUser;

    public RevokePlatformUserSessionCommandHandler(
        IPlatformUserSessionRepository sessionRepository,
        ICurrentPlatformUserContext currentUser)
    {
        _sessionRepository = sessionRepository;
        _currentUser = currentUser;
    }

    public async Task Handle(RevokePlatformUserSessionCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId == null)
            throw new UnauthorizedAccessException("Current platform user cannot be resolved.");

        var session = await _sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);
        if (session == null || session.AccountId != request.UserId)
            throw new NotFoundException($"Session {request.SessionId} not found for user {request.UserId}.");

        await _sessionRepository.RevokeByIdAsync(request.SessionId, cancellationToken);
    }
}
