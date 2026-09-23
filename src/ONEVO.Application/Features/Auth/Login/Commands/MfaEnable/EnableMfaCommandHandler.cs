using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;

using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaEnable;

public class EnableMfaCommandHandler : IRequestHandler<EnableMfaCommand, Result<MfaSetupDto>>
{
    private readonly IUserRepository _users;
    private readonly IUserMfaRepository _userMfas;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IEncryptionService _encryption;
    private readonly IDateTimeProvider _clock;

    public EnableMfaCommandHandler(
        IUserRepository users,
        IUserMfaRepository userMfas,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IEncryptionService encryption,
        IDateTimeProvider clock)
    {
        _users = users;
        _userMfas = userMfas;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _encryption = encryption;
        _clock = clock;
    }

    public async Task<Result<MfaSetupDto>> Handle(EnableMfaCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(_currentUser.UserId, cancellationToken);
        if (user is null)
            return Result<MfaSetupDto>.NotFound("User not found.");

        var existingVerified = await _userMfas.GetTotpAsync(user.Id, isVerified: true, cancellationToken);
        if (existingVerified is not null)
            return Result<MfaSetupDto>.Conflict("MFA is already enabled.");

        // Generate a 20-byte TOTP secret
        var secretBytes = new byte[20];
        System.Security.Cryptography.RandomNumberGenerator.Fill(secretBytes);
        var secretBase32 = Base32Encode(secretBytes);

        var encryptedSecret = _encryption.Encrypt(secretBase32);

        var existingSetup = await _userMfas.GetTotpAsync(user.Id, isVerified: false, cancellationToken);

        if (existingSetup is not null)
            _userMfas.Remove(existingSetup);

        await _userMfas.AddAsync(new UserMfa
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            UserId = user.Id,
            MethodType = "totp",
            Secret = encryptedSecret,
            IsVerified = false,
            CreatedAt = _clock.UtcNow
        }, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var qrCodeUri = $"otpauth://totp/ONEVO:{user.Email}?secret={secretBase32}&issuer=ONEVO";
        return Result<MfaSetupDto>.Success(new MfaSetupDto(secretBase32, qrCodeUri));
    }

    private static string Base32Encode(byte[] bytes)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new System.Text.StringBuilder();
        int buffer = 0, bitsLeft = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                bitsLeft -= 5;
                result.Append(alphabet[(buffer >> bitsLeft) & 31]);
            }
        }
        if (bitsLeft > 0)
            result.Append(alphabet[(buffer << (5 - bitsLeft)) & 31]);
        return result.ToString();
    }
}
