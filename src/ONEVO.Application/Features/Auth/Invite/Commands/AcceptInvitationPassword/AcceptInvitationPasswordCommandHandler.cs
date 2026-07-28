using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
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
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILegalAcceptanceSubmissionService _legalSubmission;
    private readonly ILoginContinuationService _continuation;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantContext _tenantContext;
    private readonly IGlobalEmailDirectoryRepository _globalDirectory;
    private readonly IUserRoleRepository _userRoles;
    private readonly IPositionRepository _positions;

    public AcceptInvitationPasswordCommandHandler(
        IInvitationTokenRepository invitations,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ILegalAcceptanceSubmissionService legalSubmission,
        ILoginContinuationService continuation,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock,
        ITenantContext tenantContext,
        IGlobalEmailDirectoryRepository globalDirectory,
        IUserRoleRepository userRoles,
        IPositionRepository positions)
    {
        _invitations = invitations;
        _users = users;
        _passwordHasher = passwordHasher;
        _legalSubmission = legalSubmission;
        _continuation = continuation;
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

        var user = await _users.GetByIdAsync(inv.UserId, ct);
        if (user is null)
            return Result<LoginResponseDto>.Failure("Invited user record not found.", 500);

        var legal = await _legalSubmission.ValidateAndStageAsync(
            inv.TenantId,
            user.Id,
            request.Acceptances,
            requireComplete: true,
            request.IpAddress,
            request.UserAgent,
            ct);
        if (!legal.IsSuccess)
            return Result<LoginResponseDto>.Failure(legal.Error!, legal.StatusCode ?? 400);

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
        await _unitOfWork.SaveChangesAsync(ct);

        return await _continuation.FinishAuthenticatedLoginAsync(
            user, "invitation_password", request.IpAddress, request.UserAgent, ct);
    }

    private static string? CheckInvitationUsable(InvitationToken inv, DateTimeOffset now)
    {
        if (inv.UsedAt is not null) return "This invitation has already been accepted.";
        if (inv.RevokedAt is not null) return "This invitation has been revoked.";
        if (inv.ExpiresAt <= now) return "This invitation has expired.";
        return null;
    }

}
