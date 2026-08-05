using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RefreshTrayToken;

public class RefreshTrayTokenCommandHandler
    : IRequestHandler<RefreshTrayTokenCommand, Result<TrayAuthResponseDto>>
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(90);
    private const int AccessTokenExpiresInSeconds = 3600;
    private const int RefreshTokenExpiresInSeconds = 7_776_000;

    private readonly ITrayActivationRepository _repository;
    private readonly ITrayTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public RefreshTrayTokenCommandHandler(
        ITrayActivationRepository repository,
        ITrayTokenService tokenService,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _tokenService = tokenService;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TrayAuthResponseDto>> Handle(
        RefreshTrayTokenCommand request,
        CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.HashToken(request.RefreshToken);
        var existingToken = await _repository.FindActiveRefreshTokenAsync(tokenHash, cancellationToken);

        if (existingToken is null)
            return Result<TrayAuthResponseDto>.Failure("Invalid or expired refresh token.", 401);

        var device = await _repository.FindActiveDeviceAsync(
            existingToken.DeviceRegistrationId, existingToken.TenantId, cancellationToken);

        if (device is null)
            return Result<TrayAuthResponseDto>.Failure("Device is no longer active.", 401);

        if (device.DeviceFingerprint != request.DeviceFingerprint)
        {
            // Fingerprint mismatch — possible token theft; revoke all tokens for this device
            await _repository.RevokeAllRefreshTokensForDeviceAsync(
                device.Id, "fingerprint_mismatch", cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TrayAuthResponseDto>.Failure("Device fingerprint mismatch.", 401);
        }

        var now = _clock.UtcNow;

        // Rotate: revoke old, issue new
        await _repository.RevokeRefreshTokenAsync(existingToken, "rotated", cancellationToken);

        var newRawToken = _tokenService.GenerateRawRefreshToken();
        var newTokenHash = _tokenService.HashToken(newRawToken);

        var newRefreshToken = new TrayDeviceRefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = existingToken.TenantId,
            UserId = existingToken.UserId,
            DeviceRegistrationId = existingToken.DeviceRegistrationId,
            TokenHash = newTokenHash,
            ExpiresAt = now.Add(RefreshTokenLifetime),
            IsRevoked = false,
            CreatedAt = now
        };

        await _repository.AddRefreshTokenAsync(newRefreshToken, cancellationToken);
        await _repository.UpdateDeviceLastSeenAsync(device.Id, now, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            device.Id, existingToken.UserId, existingToken.TenantId);

        return Result<TrayAuthResponseDto>.Success(new TrayAuthResponseDto(
            accessToken,
            AccessTokenExpiresInSeconds,
            newRawToken,
            RefreshTokenExpiresInSeconds));
    }
}
