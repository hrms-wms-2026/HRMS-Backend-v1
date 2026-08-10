using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.AcceptPlatformManagerInvite;

public sealed class AcceptPlatformManagerInviteCommandHandler : IRequestHandler<AcceptPlatformManagerInviteCommand, Result>
{
    private readonly IPlatformUserInviteRepository _invites;
    private readonly IPlatformUserRepository _users;
    private readonly IPlatformUserCredentialRepository _credentials;
    private readonly IPlatformAuthEventRepository _authEvents;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public AcceptPlatformManagerInviteCommandHandler(
        IPlatformUserInviteRepository invites,
        IPlatformUserRepository users,
        IPlatformUserCredentialRepository credentials,
        IPlatformAuthEventRepository authEvents,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokens,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _invites = invites;
        _users = users;
        _credentials = credentials;
        _authEvents = authEvents;
        _passwordHasher = passwordHasher;
        _tokens = tokens;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(AcceptPlatformManagerInviteCommand request, CancellationToken ct)
    {
        var tokenHash = _tokens.HashToken(request.RawToken);
        var invite = await _invites.GetByTokenHashAsync(tokenHash, ct);
        if (invite is null)
            return Result.NotFound("Invitation not found.");

        var now = _clock.UtcNow;
        if (invite.AcceptedAt is not null)
            return Result.Failure("This invitation has already been accepted.", 400);
        if (invite.RevokedAt is not null)
            return Result.Failure("This invitation has been revoked.", 400);
        if (invite.ExpiresAt <= now)
            return Result.Failure("This invitation has expired.", 400);

        var user = invite.PlatformUserId.HasValue
            ? await _users.GetByIdAsync(invite.PlatformUserId.Value, ct)
            : null;
        if (user is null)
            return Result.Failure("Invited user record not found.", 500);

        await _credentials.AddAsync(new PlatformUserCredential
        {
            Id = Guid.NewGuid(),
            PlatformUserId = user.Id,
            CredentialType = PlatformUserCredential.PasswordType,
            PasswordHash = _passwordHasher.Hash(request.Password),
            PasswordAlgorithm = PlatformUserCredential.BCryptAlgorithm,
            PasswordChangedAt = now,
            MustChangePassword = false,
            CreatedAt = now
        }, ct);

        user.Status = PlatformUser.StatusActive;
        user.InviteStatus = PlatformUser.InviteAccepted;
        _users.UpdateUser(user);

        invite.AcceptedAt = now;
        _invites.Update(invite);

        await _authEvents.AddAsync(new PlatformAuthEvent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            EventType = PlatformAuthEvent.PlatformManagerInviteAccepted,
            MetadataJson = "{}",
            CreatedAt = now
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
