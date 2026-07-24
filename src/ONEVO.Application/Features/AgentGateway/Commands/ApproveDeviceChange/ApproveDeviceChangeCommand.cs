using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.ApproveDeviceChange;

public sealed record ApproveDeviceChangeCommand(
    Guid RequestId,
    string? ReviewComment,
    Guid ReviewedById) : IRequest<Result>;
