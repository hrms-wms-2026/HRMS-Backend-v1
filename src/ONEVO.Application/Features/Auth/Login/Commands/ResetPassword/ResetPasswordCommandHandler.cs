using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.Login.Commands.ResetPassword;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand, Result>
{
    private readonly IPasswordResetTokenRepository _passwordResetTokens;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISecureTokenGenerator _tokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPermissionVersionService _permVersionService;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantContext _tenantContext;

    public ResetPasswordCommandHandler(
        IPasswordResetTokenRepository passwordResetTokens,
        IRefreshTokenRepository refreshTokens,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ISecureTokenGenerator tokenService,
        IPasswordHasher passwordHasher,
        IPermissionVersionService permVersionService,
        IDateTimeProvider clock,
        ITenantContext tenantContext)
    {
        _passwordResetTokens = passwordResetTokens;
        _refreshTokens = refreshTokens;
        _users = users;
        _unitOfWork = unitOfWork;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _permVersionService = permVersionService;
        _clock = clock;
        _tenantContext = tenantContext;
    }

    public async Task<Result> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)
            return Result.Failure("Invalid or expired reset token.", 400);

        var tokenHash = _tokenService.HashToken(request.Token);

        var resetToken = await _passwordResetTokens.GetResetTokenByHashAsync(tokenHash, cancellationToken);

        if (resetToken is null || !resetToken.IsValid)
            return Result.Failure("Invalid or expired reset token.", 400);

        if (resetToken.TenantId != _tenantContext.TenantId)
            return Result.Failure("Invalid or expired reset token.", 400);

        var user = await _users.GetByIdAsync(resetToken.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result.Failure("User account unavailable.", 400);

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.PasswordSetByAdmin = false;
        user.TemporaryPasswordExpiresAt = null;
        user.UpdatedAt = _clock.UtcNow;

        resetToken.UsedAt = _clock.UtcNow;

        // Revoke all existing refresh tokens (force re-login)
        var tokens = await _refreshTokens.ListActiveByUserIdAsync(user.Id, cancellationToken);
        foreach (var t in tokens)
            t.RevokedAt = _clock.UtcNow;

        await _permVersionService.IncrementVersionAsync(user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
