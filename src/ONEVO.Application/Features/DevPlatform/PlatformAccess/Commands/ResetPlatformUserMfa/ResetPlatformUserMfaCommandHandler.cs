using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.ResetPlatformUserMfa;

/// <summary>
/// Recovery for a lost authenticator now that admin MFA is mandatory (F-02, decision D2).
/// Clears the TOTP secret, returns the target to not-enrolled and revokes all of their sessions,
/// so their next password sign-in is an enrollment-only session. Writes a platform auth event
/// naming who reset it; the secret is never written anywhere.
///
/// Self-reset is refused: otherwise a stolen session could swap the second factor. The first or
/// only platform admin therefore needs the documented database runbook instead.
/// </summary>
public sealed class ResetPlatformUserMfaCommandHandler : IRequestHandler<ResetPlatformUserMfaCommand, Result>
{
    private readonly IPlatformUserRepository _users;
    private readonly IPlatformUserSessionRepository _sessions;
    private readonly IPlatformAuthEventRepository _authEvents;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public ResetPlatformUserMfaCommandHandler(
        IPlatformUserRepository users,
        IPlatformUserSessionRepository sessions,
        IPlatformAuthEventRepository authEvents,
        ICurrentPlatformUserContext currentUser,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _users = users;
        _sessions = sessions;
        _authEvents = authEvents;
        _currentUser = currentUser;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(ResetPlatformUserMfaCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } callerId)
            return Result.Failure("Not authenticated.", 401);

        if (callerId == request.PlatformUserId)
            return Result.Forbidden("You cannot reset your own two-factor authentication. Ask another platform admin.");

        var target = await _users.GetByIdAsync(request.PlatformUserId, ct);
        if (target is null)
            return Result.NotFound($"Platform user '{request.PlatformUserId}' not found.");

        var previousStatus = target.MfaStatus;
        target.MfaSecret = null;
        target.MfaStatus = PlatformUser.MfaNotEnrolled;
        _users.UpdateUser(target);

        await _sessions.RevokeAllByUserIdAsync(target.Id, ct);

        await _authEvents.AddAsync(new PlatformAuthEvent
        {
            Id = Guid.NewGuid(),
            UserId = target.Id,
            EventType = PlatformAuthEvent.MfaReset,
            MetadataJson = JsonSerializer.Serialize(new
            {
                reset_by = callerId,
                previous_mfa_status = previousStatus,
                sessions_revoked = true
            }),
            CreatedAt = _clock.UtcNow
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
