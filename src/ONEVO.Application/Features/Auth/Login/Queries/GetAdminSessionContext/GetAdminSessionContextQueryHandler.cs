using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Models.Auth;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.Queries.GetAdminSessionContext;

/// <summary>
/// Read-only session-restore check for the admin console, mirroring the tenant side's
/// GetCurrentSessionQueryHandler: it never signs in or touches AdminDatabaseTicketStore, it only
/// confirms the already-authenticated admin_session cookie still resolves to an active platform
/// user and reports their current role. Backed by [Authorize(Policy = "AdminPolicy")] at the
/// controller, so this handler only runs once that cookie has already been validated.
/// </summary>
public sealed class GetAdminSessionContextQueryHandler
    : IRequestHandler<GetAdminSessionContextQuery, Result<AdminSessionResponseDto>>
{
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly IPlatformUserRepository _platformUsers;
    private readonly IPlatformPermissionResolver _permissionResolver;
    private readonly IDateTimeProvider _clock;

    public GetAdminSessionContextQueryHandler(
        ICurrentPlatformUserContext currentUser,
        IPlatformUserRepository platformUsers,
        IPlatformPermissionResolver permissionResolver,
        IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _platformUsers = platformUsers;
        _permissionResolver = permissionResolver;
        _clock = clock;
    }

    public async Task<Result<AdminSessionResponseDto>> Handle(
        GetAdminSessionContextQuery request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result<AdminSessionResponseDto>.Failure("Authentication required.", 401);

        var user = await _platformUsers.GetByIdAsync(userId, ct);
        if (user is null)
            return Result<AdminSessionResponseDto>.Failure("Authentication required.", 401);

        var profile = await _permissionResolver.ResolveActiveUserAsync(user.Id, ct);
        if (profile is null)
            return Result<AdminSessionResponseDto>.Failure("This account is not active.", 403);

        return Result<AdminSessionResponseDto>.Success(new AdminSessionResponseDto(
            PlatformUserId: user.Id,
            Email: user.Email,
            PlatformRole: profile.RoleNames.Count > 0 ? profile.RoleNames[0] : string.Empty,
            ExpiresAt: _clock.UtcNow.Add(SessionPolicy.SlidingWindow),
            MfaRequired: false));
    }
}
