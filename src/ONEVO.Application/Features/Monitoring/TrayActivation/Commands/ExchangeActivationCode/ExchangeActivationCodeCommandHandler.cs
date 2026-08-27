using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.Services;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ExchangeActivationCode;

public class ExchangeActivationCodeCommandHandler
    : IRequestHandler<ExchangeActivationCodeCommand, Result<TrayAuthResponseDto>>
{
    private readonly ITrayActivationRepository _repository;
    private readonly ITrayTokenService _tokenService;
    private readonly ITrayEnrollmentService _enrollmentService;
    private readonly IUnitOfWork _unitOfWork;

    public ExchangeActivationCodeCommandHandler(
        ITrayActivationRepository repository,
        ITrayTokenService tokenService,
        ITrayEnrollmentService enrollmentService,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _tokenService = tokenService;
        _enrollmentService = enrollmentService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TrayAuthResponseDto>> Handle(
        ExchangeActivationCodeCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedCode = request.Code.Trim().ToUpperInvariant();
        var codeHash = _tokenService.HashToken(normalizedCode);

        return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            // TenantId is unknown at this stage — the code lookup must match globally
            // within the hashed code; the repository filters by hash only here.
            var activationCode = await _repository.FindActiveCodeByHashAsync(codeHash, ct);
            if (activationCode is null)
                return Result<TrayAuthResponseDto>.Failure(
                    "Invalid or expired activation code.", 401, "invalid_activation_code");

            await _repository.MarkCodeUsedAsync(activationCode, ct);
            await _repository.RevokeActiveRegistrationsForIdentityAsync(
                activationCode.TenantId,
                activationCode.UserId,
                request.DeviceFingerprint,
                "replaced_by_new_activation",
                ct);

            var credentials = await _enrollmentService.IssueAsync(
                new TrayEnrollmentRequest(
                    activationCode.TenantId,
                    activationCode.UserId,
                    request.DeviceName,
                    request.DeviceOs,
                    request.DeviceFingerprint),
                ct);

            await _unitOfWork.SaveChangesAsync(ct);
            return Result<TrayAuthResponseDto>.Success(credentials);
        }, cancellationToken);
    }
}
