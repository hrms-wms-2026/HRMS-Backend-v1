using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;

/// <summary>
/// Creates the PlatformUser row (Pending) and its roles immediately, rather than at
/// acceptance time - see docs/superpowers/specs/2026-08-06-invite-platform-manager-design.md
/// "Data model change" for why this replaces an earlier join-table design.
/// </summary>
public sealed class InvitePlatformManagerCommandHandler : IRequestHandler<InvitePlatformManagerCommand, Result>
{
    private static readonly TimeSpan InviteValidity = TimeSpan.FromHours(72);

    private readonly IPlatformUserRepository _users;
    private readonly IPlatformRoleRepository _roles;
    private readonly IPlatformUserInviteRepository _invites;
    private readonly IPlatformAuthEventRepository _authEvents;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly ISecureTokenGenerator _tokens;
    private readonly IOutboxWriter _outbox;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public InvitePlatformManagerCommandHandler(
        IPlatformUserRepository users,
        IPlatformRoleRepository roles,
        IPlatformUserInviteRepository invites,
        IPlatformAuthEventRepository authEvents,
        ICurrentPlatformUserContext currentUser,
        ISecureTokenGenerator tokens,
        IOutboxWriter outbox,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _users = users;
        _roles = roles;
        _invites = invites;
        _authEvents = authEvents;
        _currentUser = currentUser;
        _tokens = tokens;
        _outbox = outbox;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(InvitePlatformManagerCommand request, CancellationToken ct)
    {
        if (request.RoleIds.Count == 0)
            return Result.Failure("At least one role is required.", 400);

        foreach (var roleId in request.RoleIds)
        {
            var role = await _roles.GetRoleByIdAsync(roleId, ct);
            if (role is null)
                return Result.NotFound($"Role '{roleId}' not found.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var existing = await _users.GetByEmailAsync(email, ct);
        if (existing is not null)
            return Result.Conflict($"A platform user with email '{email}' already exists or has a pending invite.");

        var now = _clock.UtcNow;
        var fullName = request.FullName.Trim();

        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName,
            Status = PlatformUser.StatusPending,
            InviteStatus = PlatformUser.InvitePending,
            CreatedAt = now
        };
        await _users.AddAsync(user, ct);

        // ReplaceRolesAsync removes any existing roles for the user then adds the
        // given set; on this brand-new user there's nothing to remove, so it's
        // equivalent to a plain bulk-add without needing a separate repository method.
        await _users.ReplaceRolesAsync(user.Id, request.RoleIds, ct);

        var invitedById = _currentUser.UserId
            ?? throw new InvalidOperationException(
                "InvitePlatformManagerCommand requires an authenticated platform user.");

        var rawToken = _tokens.GenerateOpaqueToken();
        var invite = new PlatformUserInvite
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = fullName,
            InviteTokenHash = _tokens.HashToken(rawToken),
            InvitedById = invitedById,
            PlatformUserId = user.Id,
            ExpiresAt = now.Add(InviteValidity),
            CreatedAt = now
        };
        await _invites.AddAsync(invite, ct);

        await _outbox.EnqueueAsync(
            OutboxMessageTypes.PlatformManagerInviteEmail,
            new PlatformManagerInviteEmailPayload(email, fullName, rawToken),
            tenantId: null,
            ct);

        await _authEvents.AddAsync(new PlatformAuthEvent
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            EventType = PlatformAuthEvent.PlatformManagerInvited,
            MetadataJson = "{}",
            CreatedAt = now
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
