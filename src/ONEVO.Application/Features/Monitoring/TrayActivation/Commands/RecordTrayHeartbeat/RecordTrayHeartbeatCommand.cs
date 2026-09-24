using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.RecordTrayHeartbeat;

public sealed record RecordTrayHeartbeatCommand : IRequest<Result>;
