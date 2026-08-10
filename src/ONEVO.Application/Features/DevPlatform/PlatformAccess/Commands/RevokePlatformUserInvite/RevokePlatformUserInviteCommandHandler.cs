using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserInvite;

public sealed class RevokePlatformUserInviteCommandHandler : IRequestHandler<RevokePlatformUserInviteCommand, Result>
{
    private readonly IPlatformUserInviteRepository _invites;
    private readonly IPlatformUserRepository _users;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public RevokePlatformUserInviteCommandHandler(
        IPlatformUserInviteRepository invites,
        IPlatformUserRepository users,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _invites = invites;
        _users = users;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(RevokePlatformUserInviteCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.PlatformUserId, ct);
        if (user is null)
            return Result.NotFound($"Platform user '{request.PlatformUserId}' not found.");

        var invite = await _invites.GetByPlatformUserIdAsync(request.PlatformUserId, ct);
        if (invite is null)
            return Result.NotFound($"No invite found for platform user '{request.PlatformUserId}'.");

        var now = _clock.UtcNow;
        invite.RevokedAt = now;
        _invites.Update(invite);

        user.Status = PlatformUser.StatusInactive;
        user.InviteStatus = PlatformUser.InviteRevoked;
        _users.UpdateUser(user);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
