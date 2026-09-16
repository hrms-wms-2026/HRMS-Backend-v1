using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;

public sealed record ApproveDeviceChangeRequestCommand(
    Guid Id, string? ReviewComment) : IRequest<Result<DeviceChangeRequestResponse>>;

public sealed record RejectDeviceChangeRequestCommand(
    Guid Id, string? ReviewComment) : IRequest<Result<DeviceChangeRequestResponse>>;
