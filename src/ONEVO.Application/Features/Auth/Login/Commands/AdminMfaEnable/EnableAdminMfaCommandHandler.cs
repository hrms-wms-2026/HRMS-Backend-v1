using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.Utils;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.AdminMfaEnable;

/// <summary>
/// Begins MFA setup for the calling platform user. Unlike the tenant flow (a separate UserMfa row
/// per method), platform users carry the encrypted TOTP secret directly on PlatformUser.MfaSecret —
/// PlatformUser.MfaStatus already existed as a flag-based lifecycle field before this feature, so
/// this follows that existing design rather than introducing a parallel child-table pattern.
/// MfaStatus only transitions to Enrolled once the caller proves possession of the secret via
/// EnableAdminMfaCommand's companion, VerifyAdminMfaCommand — generating a secret here does not by
/// itself turn MFA on.
/// </summary>
public sealed class EnableAdminMfaCommandHandler : IRequestHandler<EnableAdminMfaCommand, Result<MfaSetupDto>>
{
    private readonly IPlatformUserRepository _platformUsers;
    private readonly ICurrentPlatformUserContext _currentUser;
    private readonly IEncryptionService _encryption;
    private readonly IUnitOfWork _uow;

    public EnableAdminMfaCommandHandler(
        IPlatformUserRepository platformUsers,
        ICurrentPlatformUserContext currentUser,
        IEncryptionService encryption,
        IUnitOfWork uow)
    {
        _platformUsers = platformUsers;
        _currentUser = currentUser;
        _encryption = encryption;
        _uow = uow;
    }

    public async Task<Result<MfaSetupDto>> Handle(EnableAdminMfaCommand request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
            return Result<MfaSetupDto>.Failure("Not authenticated.", 401);

        var user = await _platformUsers.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            return Result<MfaSetupDto>.NotFound("User not found.");

        if (user.MfaStatus == PlatformUser.MfaEnrolled)
            return Result<MfaSetupDto>.Conflict("MFA is already enabled.");

        var (secret, qrCodeUri) = TotpSecretGenerator.Generate(user.Email, "ONEVO Admin");

        user.MfaSecret = _encryption.Encrypt(secret);
        user.MfaStatus = PlatformUser.MfaNotEnrolled;
        _platformUsers.UpdateUser(user);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<MfaSetupDto>.Success(new MfaSetupDto(secret, qrCodeUri));
    }
}
