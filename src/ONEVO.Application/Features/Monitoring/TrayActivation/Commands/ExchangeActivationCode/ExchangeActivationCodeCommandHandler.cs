using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ExchangeActivationCode;

public class ExchangeActivationCodeCommandHandler
    : IRequestHandler<ExchangeActivationCodeCommand, Result<TrayAuthResponseDto>>
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(90);
    private const int AccessTokenExpiresInSeconds = 3600;
    private const int RefreshTokenExpiresInSeconds = 7_776_000; // 90 days

    private readonly ITrayActivationRepository _repository;
    private readonly ITrayTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ExchangeActivationCodeCommandHandler(
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
        ExchangeActivationCodeCommand request,
        CancellationToken cancellationToken)
    {
        var codeHash = _tokenService.HashToken(request.Code);

        // TenantId is unknown at this stage — the code lookup must match globally
        // within the hashed code; the repository filters by hash only here.
        var activationCode = await _repository.FindActiveCodeByHashAsync(codeHash, cancellationToken);

        if (activationCode is null)
            return Result<TrayAuthResponseDto>.Failure("Invalid or expired activation code.", 401);

        var now = _clock.UtcNow;

        await _repository.MarkCodeUsedAsync(activationCode, cancellationToken);

        var device = new TrayDeviceRegistration
        {
            Id = Guid.NewGuid(),
            TenantId = activationCode.TenantId,
            UserId = activationCode.UserId,
            DeviceName = request.DeviceName,
            DeviceOs = request.DeviceOs,
            DeviceFingerprint = request.DeviceFingerprint,
            IsActive = true,
            ActivatedAt = now,
            CreatedAt = now
        };

        await _repository.AddDeviceRegistrationAsync(device, cancellationToken);

        var rawRefreshToken = _tokenService.GenerateRawRefreshToken();
        var refreshTokenHash = _tokenService.HashToken(rawRefreshToken);

        var refreshToken = new TrayDeviceRefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = activationCode.TenantId,
            UserId = activationCode.UserId,
            DeviceRegistrationId = device.Id,
            TokenHash = refreshTokenHash,
            ExpiresAt = now.Add(RefreshTokenLifetime),
            IsRevoked = false,
            CreatedAt = now
        };

        await _repository.AddRefreshTokenAsync(refreshToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(
            device.Id, activationCode.UserId, activationCode.TenantId);

        return Result<TrayAuthResponseDto>.Success(new TrayAuthResponseDto(
            accessToken,
            AccessTokenExpiresInSeconds,
            rawRefreshToken,
            RefreshTokenExpiresInSeconds));
    }
}
