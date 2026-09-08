using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Queries.GetDeviceAuthorizationPreview;

public sealed record GetDeviceAuthorizationPreviewQuery(
    Guid RequestId,
    string UserCode) : IRequest<Result<DeviceAuthorizationPreviewDto>>;
