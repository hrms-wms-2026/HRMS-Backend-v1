using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ApproveDeviceAuthorization;

public sealed class ApproveDeviceAuthorizationCommandHandler
    : IRequestHandler<ApproveDeviceAuthorizationCommand, Result>
{
    private readonly ITrayActivationRepository _repository;
    private readonly ITrayTokenService _tokenService;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveDeviceAuthorizationCommandHandler(
        ITrayActivationRepository repository,
        ITrayTokenService tokenService,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _tokenService = tokenService;
        _currentUser = currentUser;
        _clock = clock;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ApproveDeviceAuthorizationCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden("Authentication required.");

        if (_currentUser.LegalEntityId is not Guid legalEntityId)
            return Result.UnprocessableEntity(
                "Select an active company before approving this device.");

        var authorization = await _repository.FindDeviceAuthorizationForApprovalAsync(
            request.RequestId,
            _tokenService.HashToken(request.UserCode),
            ct);

        if (authorization is null)
            return Result.Failure("Device authorization request is not available.", 400, "access_denied");

        authorization.Status = Domain.Features.Monitoring.TrayActivation.Enums.DeviceAuthorizationStatus.Approved;
        authorization.ApprovedTenantId = _currentUser.TenantId;
        authorization.ApprovedUserId = _currentUser.UserId;
        authorization.ApprovedLegalEntityId = legalEntityId;
        authorization.ApprovedAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
