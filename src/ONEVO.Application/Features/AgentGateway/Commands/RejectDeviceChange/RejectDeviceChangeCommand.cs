using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.RejectDeviceChange;

public sealed record RejectDeviceChangeCommand(
    Guid RequestId,
    string? ReviewComment,
    Guid ReviewedById) : IRequest<Result>;
