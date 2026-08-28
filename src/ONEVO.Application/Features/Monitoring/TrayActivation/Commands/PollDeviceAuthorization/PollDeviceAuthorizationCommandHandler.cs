using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Services;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Enums;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.PollDeviceAuthorization;

public sealed class PollDeviceAuthorizationCommandHandler
    : IRequestHandler<PollDeviceAuthorizationCommand, Result<TrayAuthResponseDto>>
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ITrayActivationRepository _repository;
    private readonly ITrayTokenService _tokenService;
    private readonly ITrayEnrollmentService _enrollmentService;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public PollDeviceAuthorizationCommandHandler(
        ITrayActivationRepository repository,
        ITrayTokenService tokenService,
        ITrayEnrollmentService enrollmentService,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _tokenService = tokenService;
        _enrollmentService = enrollmentService;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TrayAuthResponseDto>> Handle(
        PollDeviceAuthorizationCommand request,
        CancellationToken ct)
    {
        var deviceCodeHash = _tokenService.HashToken(request.DeviceCode);
        var fingerprintHash = _tokenService.HashToken(request.DeviceFingerprint);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var authorization = await _repository.LockDeviceAuthorizationForPollAsync(deviceCodeHash, innerCt);
            if (authorization is null)
                return Failure("access_denied", "Device authorization is not available.");

            var now = _clock.UtcNow;
            if (authorization.IsExpired(now) && authorization.Status is not DeviceAuthorizationStatus.Consumed)
            {
                authorization.Status = DeviceAuthorizationStatus.Expired;
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Failure("expired_token", "Device authorization has expired.");
            }

            if (authorization.LastPolledAt is not null
                && now < authorization.LastPolledAt.Value.Add(PollInterval))
            {
                authorization.PollViolationCount++;
                authorization.LastPolledAt = now;
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Failure("slow_down", "Polling too frequently.");
            }

            authorization.LastPolledAt = now;

            if (authorization.Status == DeviceAuthorizationStatus.Pending)
            {
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Failure("authorization_pending", "Authorization is pending.");
            }

            if (authorization.Status != DeviceAuthorizationStatus.Approved
                || authorization.ApprovedTenantId is null
                || authorization.ApprovedUserId is null
                || authorization.DeviceFingerprintHash != fingerprintHash)
            {
                await _unitOfWork.SaveChangesAsync(innerCt);
                return Failure("access_denied", "Device authorization was denied.");
            }

            var credentials = await _enrollmentService.IssueAsync(
                new TrayEnrollmentRequest(
                    authorization.ApprovedTenantId.Value,
                    authorization.ApprovedUserId.Value,
                    authorization.DeviceName,
                    authorization.DeviceOs,
                    request.DeviceFingerprint),
                innerCt);

            authorization.Status = DeviceAuthorizationStatus.Consumed;
            authorization.ConsumedAt = now;
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<TrayAuthResponseDto>.Success(credentials);
        }, ct);
    }

    private static Result<TrayAuthResponseDto> Failure(string code, string message)
        => Result<TrayAuthResponseDto>.Failure(message, 400, code);
}
