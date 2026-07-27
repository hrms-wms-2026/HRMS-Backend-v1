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
    private const string GenericTokenError = "Invalid or expired reset token.";

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
            return Result.Failure(GenericTokenError, 400);

        var tokenHash = _tokenService.HashToken(request.Token);
        var tenantId = _tenantContext.TenantId;

        return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var now = _clock.UtcNow;

            // Atomic consume: a single UPDATE ... WHERE used_at IS NULL guard (see
            // EfAuthRepository.TryConsumeResetTokenAsync) lets exactly one concurrent request win.
            // This transaction still commits on the user-missing/inactive branch below, burning the
            // token even though the reset did not succeed - a deliberate fail-safe tradeoff so a
            // stale token for a deleted/deactivated user cannot be replayed indefinitely. See
            // PASSWORD_RESET_PRODUCTION_READINESS_REPORT.md.
            var userId = await _passwordResetTokens.TryConsumeResetTokenAsync(tokenHash, tenantId, now, ct);
            if (userId is null)
                return Result.Failure(GenericTokenError, 400);

            var user = await _users.GetByIdAsync(userId.Value, ct);
            if (user is null || !user.IsActive)
                return Result.Failure(GenericTokenError, 400);

            user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
            user.MustChangePassword = false;
            user.PasswordSetByAdmin = false;
            user.TemporaryPasswordExpiresAt = null;
            user.UpdatedAt = now;

            // Revoke all existing refresh tokens (force re-login)
            var tokens = await _refreshTokens.ListActiveByUserIdAsync(user.Id, ct);
            foreach (var t in tokens)
                t.RevokedAt = now;

            await _permVersionService.IncrementVersionAsync(user.Id, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return Result.Success();
        }, cancellationToken);
    }
}
