using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Common.Models;

using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.Mappers;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResponseDto>>
{
    private readonly IUserRepository _users;
    private readonly IUserMfaRepository _userMfas;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMfaChallengeStore _mfaChallenges;
    private readonly ILoginSessionMaterialFactory _sessionMaterialFactory;
    private readonly ITenantContext _tenantContext;

    public LoginCommandHandler(
        IUserRepository users,
        IUserMfaRepository userMfas,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IMfaChallengeStore mfaChallenges,
        ILoginSessionMaterialFactory sessionMaterialFactory,
        ITenantContext tenantContext)
    {
        _users = users;
        _userMfas = userMfas;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _mfaChallenges = mfaChallenges;
        _sessionMaterialFactory = sessionMaterialFactory;
        _tenantContext = tenantContext;
    }

    public async Task<Result<LoginResponseDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)
            return Result<LoginResponseDto>.Failure("Tenant context is not resolved.", 400);

        if (_tenantContext.Status is not (TenantStatus.Active or TenantStatus.Trial))
            return Result<LoginResponseDto>.Failure("This tenant is not available.", 403);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _users.GetByTenantAndEmailAsync(_tenantContext.TenantId, normalizedEmail, cancellationToken);

        if (user is null)
            return Result<LoginResponseDto>.Failure("Invalid email or password.");

        if (!user.IsActive)
            return Result<LoginResponseDto>.Failure("Account is deactivated. Contact your administrator.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            return Result<LoginResponseDto>.Failure("Invalid email or password.");

        // Forced password change
        if (user.MustChangePassword)
        {
            if (user.PasswordSetByAdmin && user.TemporaryPasswordExpiresAt.HasValue
                && user.TemporaryPasswordExpiresAt <= DateTimeOffset.UtcNow)
            {
                return Result<LoginResponseDto>.Failure("Temporary password has expired. Request a new one from your administrator.");
            }

            return Result<LoginResponseDto>.Success(LoginMapper.ToPasswordChangeRequired(user));
        }

        // MFA check — derived from user_mfa table, not a flag on users
        var mfaRecord = await _userMfas.GetTotpAsync(user.Id, isVerified: true, cancellationToken);
        if (mfaRecord is not null)
        {
            var mfaChallenge = await _mfaChallenges.CreateAsync(
                user.Id,
                user.TenantId,
                TimeSpan.FromMinutes(10),
                cancellationToken);
            return Result<LoginResponseDto>.Success(LoginMapper.ToMfaRequired(user, mfaChallenge));
        }

        var result = await _sessionMaterialFactory.PrepareAsync(user, request.IpAddress, request.UserAgent, cancellationToken);
        if (result.IsSuccess)
        {
            user.LastLoginAt = DateTimeOffset.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return result;
    }
}
