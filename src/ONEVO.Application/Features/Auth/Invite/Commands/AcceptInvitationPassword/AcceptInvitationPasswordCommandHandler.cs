using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Invite.Commands.AcceptInvitationPassword;

public sealed class AcceptInvitationPasswordCommandHandler
    : IRequestHandler<AcceptInvitationPasswordCommand, Result<LoginResponseDto>>
{
    private readonly IInvitationTokenRepository _invitations;
    private readonly ITenantAuthPolicyRepository _policies;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginSessionMaterialFactory _sessionMaterialFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantContext _tenantContext;
    private readonly IGlobalEmailDirectoryRepository _globalDirectory;
    private readonly IUserRoleRepository _userRoles;
    private readonly IPositionRepository _positions;

    public AcceptInvitationPasswordCommandHandler(
        IInvitationTokenRepository invitations,
        ITenantAuthPolicyRepository policies,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ILoginSessionMaterialFactory sessionMaterialFactory,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        ITenantContext tenantContext,
        IGlobalEmailDirectoryRepository globalDirectory,
        IUserRoleRepository userRoles,
        IPositionRepository positions)
    {
        _invitations = invitations;
        _policies = policies;
        _users = users;
        _passwordHasher = passwordHasher;
        _sessionMaterialFactory = sessionMaterialFactory;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _tenantContext = tenantContext;
        _globalDirectory = globalDirectory;
        _userRoles = userRoles;
        _positions = positions;
    }

    public async Task<Result<LoginResponseDto>> Handle(
        AcceptInvitationPasswordCommand request,
        CancellationToken ct)
    {
        if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)
            return Result<LoginResponseDto>.NotFound("Invitation not found.");

        var hash = InvitationTokenHasher.Hash(request.RawToken);
        var inv = await _invitations.GetByTokenHashAsync(hash, ct);
        if (inv is null)
            return Result<LoginResponseDto>.NotFound("Invitation not found.");

        if (inv.TenantId != _tenantContext.TenantId)
            return Result<LoginResponseDto>.NotFound("Invitation not found.");

        var now = _clock.UtcNow;
        var check = CheckInvitationUsable(inv, now);
        if (check is not null)
            return Result<LoginResponseDto>.Failure(check, 400);

        if (!PasswordMethodAllowed(inv, await _policies.GetByTenantIdAsync(inv.TenantId, ct)))
            return Result<LoginResponseDto>.Failure(
                "Password completion is not enabled for this invitation.", 403);

        var user = await _users.GetByIdAsync(inv.UserId, ct);
        if (user is null)
            return Result<LoginResponseDto>.Failure("Invited user record not found.", 500);

        user.PasswordHash = _passwordHasher.Hash(request.Password);
        user.IsActive = true;
        user.MustChangePassword = false;
        user.PasswordSetByAdmin = false;
        user.TemporaryPasswordExpiresAt = null;
        user.UpdatedAt = now;

        inv.UsedAt = now;
        inv.CompletedWith = "password";

        if (inv.PositionId.HasValue)
        {
            var position = await _positions.GetByIdAsync(inv.PositionId.Value, ct);
            if (position?.DefaultRoleId.HasValue == true)
                await _userRoles.AddAsync(new UserRole
                {
                    TenantId = inv.TenantId,
                    UserId = user.Id,
                    RoleId = position.DefaultRoleId.Value,
                    AssignedAt = now,
                    AssignedBy = user.Id
                }, ct);
        }

        await _globalDirectory.UpsertAsync(inv.InvitedEmail, inv.TenantId, ct);
        user.LastLoginAt = now;
        await _unitOfWork.SaveChangesAsync(ct);

        return await _sessionMaterialFactory.PrepareAsync(user, request.IpAddress, request.UserAgent, ct);
    }

    private static string? CheckInvitationUsable(InvitationToken inv, DateTimeOffset now)
    {
        if (inv.UsedAt is not null) return "This invitation has already been accepted.";
        if (inv.RevokedAt is not null) return "This invitation has been revoked.";
        if (inv.ExpiresAt <= now) return "This invitation has expired.";
        return null;
    }

    private static bool PasswordMethodAllowed(InvitationToken inv, TenantAuthPolicy? policy)
    {
        if (string.IsNullOrWhiteSpace(inv.CompletionMethodsJson))
            return policy?.PasswordCompletionAllowed ?? true;

        try
        {
            var methods = JsonSerializer.Deserialize<List<string>>(inv.CompletionMethodsJson);
            if (methods is null || methods.Count == 0)
                return policy?.PasswordCompletionAllowed ?? true;
            return methods.Any(m => string.Equals(m, "password", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return policy?.PasswordCompletionAllowed ?? true;
        }
    }
}
