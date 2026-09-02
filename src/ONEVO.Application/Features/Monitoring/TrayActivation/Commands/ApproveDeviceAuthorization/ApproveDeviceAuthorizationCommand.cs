using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.ApproveDeviceAuthorization;

public sealed record ApproveDeviceAuthorizationCommand(
    Guid RequestId,
    string UserCode) : IRequest<Result>;
