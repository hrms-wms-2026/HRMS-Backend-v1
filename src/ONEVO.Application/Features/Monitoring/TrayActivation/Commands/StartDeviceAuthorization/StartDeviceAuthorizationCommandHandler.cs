using MediatR;
using Microsoft.Extensions.Configuration;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using System.Security.Cryptography;
using System.Text;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.StartDeviceAuthorization;

public sealed class StartDeviceAuthorizationCommandHandler
    : IRequestHandler<StartDeviceAuthorizationCommand, Result<StartDeviceAuthorizationResponseDto>>
{
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan FingerprintWindow = TimeSpan.FromHours(1);
    private const int MaxRequestsPerFingerprint = 10;
    private const int ExpiresInSeconds = 600;
    private const int PollIntervalSeconds = 5;
    private const string UserCodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private readonly ITrayActivationRepository _repository;
    private readonly ITrayTokenService _tokenService;
    private readonly IDateTimeProvider _clock;
    private readonly IConfiguration _configuration;

    public StartDeviceAuthorizationCommandHandler(
        ITrayActivationRepository repository,
        ITrayTokenService tokenService,
        IDateTimeProvider clock,
        IConfiguration configuration)
    {
        _repository = repository;
        _tokenService = tokenService;
        _clock = clock;
        _configuration = configuration;
    }

    public async Task<Result<StartDeviceAuthorizationResponseDto>> Handle(
        StartDeviceAuthorizationCommand request,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var fingerprintHash = _tokenService.HashToken(request.DeviceFingerprint);
        var recentCount = await _repository.CountRecentDeviceAuthorizationRequestsAsync(
            fingerprintHash, now.Subtract(FingerprintWindow), ct);

        if (recentCount >= MaxRequestsPerFingerprint)
        {
            return Result<StartDeviceAuthorizationResponseDto>.Failure(
                "Too many device authorization requests.",
                429,
                "rate_limited");
        }

        var deviceCode = _tokenService.GenerateOpaqueToken(32);
        var userCode = GenerateUserCode();
        var authorization = new TrayDeviceAuthorization
        {
            Id = Guid.NewGuid(),
            DeviceCodeHash = _tokenService.HashToken(deviceCode),
            UserCodeHash = _tokenService.HashToken(userCode),
            DeviceFingerprintHash = fingerprintHash,
            DeviceName = request.DeviceName,
            DeviceOs = request.DeviceOs,
            ClientVersion = request.ClientVersion,
            CreatedAt = now,
            ExpiresAt = now.Add(AuthorizationLifetime),
        };

        await _repository.AddDeviceAuthorizationAsync(authorization, ct);

        var baseUrl = (_configuration["Urls:AppBaseUrl"] ?? "https://localhost").TrimEnd('/');
        var verificationUri = $"{baseUrl}/device/activate";
        var completeUri = $"{verificationUri}?request_id={Uri.EscapeDataString(authorization.Id.ToString())}&user_code={Uri.EscapeDataString(userCode)}";

        return Result<StartDeviceAuthorizationResponseDto>.Success(
            new StartDeviceAuthorizationResponseDto(
                deviceCode,
                userCode,
                verificationUri,
                completeUri,
                ExpiresInSeconds,
                PollIntervalSeconds));
    }

    private static string GenerateUserCode()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = UserCodeAlphabet[bytes[i] % UserCodeAlphabet.Length];
        return new string(chars);
    }
}
