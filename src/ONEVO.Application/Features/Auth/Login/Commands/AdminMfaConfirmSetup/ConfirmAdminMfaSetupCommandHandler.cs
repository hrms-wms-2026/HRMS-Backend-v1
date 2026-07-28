using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminMfaConfirmSetup;

/// <summary>
/// Mirrors the tenant ConfirmMfaSetupCommandHandler pattern: runs on an already-authenticated
/// session, proves possession of the just-generated secret, and flips the enrollment flag. No
/// login challenge is involved here.
/// </summary>
public sealed class ConfirmAdminMfaSetupCommandHandler : IRequestHandler<ConfirmAdminMfaSetupCommand, Result>
{
    private readonly IPlatformUserRepository _platformUsers;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly IEncryptionService _encryption;
    private readonly ITotpService _totpService;
    private readonly IUnitOfWork _uow;

    public ConfirmAdminMfaSetupCommandHandler(
        IPlatformUserRepository platformUsers,
        ICurrentPlatformUserContext currentUser,
        IEncryptionService encryption,
        ITotpService totpService,
        IUnitOfWork uow)
    {
        _platformUsers = platformUsers;
        _currentUser = currentUser;
        _encryption = encryption;
        _totpService = totpService;
        _uow = uow;
    }

    public async Task<Result> Handle(ConfirmAdminMfaSetupCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure("Not authenticated.", 401);

        var user = await _platformUsers.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result.NotFound("User not found.");

        if (string.IsNullOrEmpty(user.MfaSecret))
            return Result.Failure("No pending MFA setup exists.", 400);

        if (user.MfaStatus == PlatformUser.MfaEnrolled)
            return Result.Conflict("MFA is already enabled.");

        var secret = _encryption.Decrypt(user.MfaSecret);
        if (!_totpService.Verify(secret, request.Code))
            return Result.Failure("Invalid MFA code.", 400);

        user.MfaStatus = PlatformUser.MfaEnrolled;
        _platformUsers.UpdateUser(user);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
