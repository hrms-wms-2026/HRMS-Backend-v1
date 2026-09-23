using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Queries.GetDeviceAuthorizationPreview;

public sealed class GetDeviceAuthorizationPreviewQueryHandler
    : IRequestHandler<GetDeviceAuthorizationPreviewQuery, Result<DeviceAuthorizationPreviewDto>>
{
    private readonly ITrayActivationRepository _repository;
    private readonly ITrayTokenService _tokenService;

    public GetDeviceAuthorizationPreviewQueryHandler(
        ITrayActivationRepository repository,
        ITrayTokenService tokenService)
    {
        _repository = repository;
        _tokenService = tokenService;
    }

    public async Task<Result<DeviceAuthorizationPreviewDto>> Handle(
        GetDeviceAuthorizationPreviewQuery request,
        CancellationToken ct)
    {
        var authorization = await _repository.FindDeviceAuthorizationForApprovalAsync(
            request.RequestId,
            _tokenService.HashToken(request.UserCode),
            ct);

        if (authorization is null)
            return Result<DeviceAuthorizationPreviewDto>.NotFound("Device authorization request not found.");

        return Result<DeviceAuthorizationPreviewDto>.Success(
            new DeviceAuthorizationPreviewDto(
                authorization.Id,
                authorization.DeviceName,
                authorization.DeviceOs,
                authorization.ClientVersion,
                authorization.ExpiresAt,
                authorization.Status.ToString()));
    }
}
