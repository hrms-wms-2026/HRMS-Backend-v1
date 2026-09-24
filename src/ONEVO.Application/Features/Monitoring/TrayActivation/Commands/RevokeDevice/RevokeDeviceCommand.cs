using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RevokeDevice;

public record RevokeDeviceCommand : IRequest<Result>;
