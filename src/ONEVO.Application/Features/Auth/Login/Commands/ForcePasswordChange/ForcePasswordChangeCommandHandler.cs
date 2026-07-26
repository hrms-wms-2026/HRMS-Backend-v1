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
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Commands.ForcePasswordChange;

public class ForcePasswordChangeCommandHandler : IRequestHandler<ForcePasswordChangeCommand, Result<LoginResponseDto>>
{
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPermissionVersionService _permVersionService;
    private readonly ILoginContinuationService _continuation;
    private readonly IDateTimeProvider _clock;
    private readonly ITenantContext _tenantContext;

    public ForcePasswordChangeCommandHandler(
        IUserRepository users,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IPermissionVersionService permVersionService,
        ILoginContinuationService continuation,
        IDateTimeProvider clock,
        ITenantContext tenantContext)
    {
        _users = users;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _permVersionService = permVersionService;
        _continuation = continuation;
        _clock = clock;
        _tenantContext = tenantContext;
    }

    public async Task<Result<LoginResponseDto>> Handle(ForcePasswordChangeCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)
            return Result<LoginResponseDto>.Failure("Invalid credentials.", 401);

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByTenantAndEmailAsync(_tenantContext.TenantId, email, cancellationToken);

        if (user is null || !user.IsActive)
            return Result<LoginResponseDto>.Failure("Invalid credentials.", 401);

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result<LoginResponseDto>.Failure("Invalid credentials.", 401);

        if (!user.MustChangePassword)
            return Result<LoginResponseDto>.Failure("Password change not required.", 400);

        if (user.PasswordSetByAdmin && user.TemporaryPasswordExpiresAt.HasValue
            && user.TemporaryPasswordExpiresAt <= _clock.UtcNow)
            return Result<LoginResponseDto>.Failure("Temporary password has expired. Request a new one.", 400);

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.MustChangePassword = false;
        user.PasswordSetByAdmin = false;
        user.TemporaryPasswordExpiresAt = null;
        user.UpdatedAt = _clock.UtcNow;

        await _permVersionService.IncrementVersionAsync(user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await _continuation.FinishAuthenticatedLoginAsync(
            user,
            "force_password_change",
            request.IpAddress,
            request.UserAgent,
            cancellationToken);
    }
}
