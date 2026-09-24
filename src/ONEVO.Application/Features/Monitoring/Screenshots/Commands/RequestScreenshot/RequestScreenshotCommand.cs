using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Screenshots.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.RequestScreenshot;

public record RequestScreenshotCommand(Guid AgentDeviceId, string? Note)
    : IRequest<Result<AgentCommandDto>>;
