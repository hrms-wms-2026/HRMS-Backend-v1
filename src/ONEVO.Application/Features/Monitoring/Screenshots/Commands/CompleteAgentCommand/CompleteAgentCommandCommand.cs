using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.CompleteAgentCommand;

public record CompleteAgentCommandCommand(
    Guid CommandId,
    bool Success,
    string? ResultJson,
    Guid? FileRecordId,
    DateTimeOffset CapturedAt) : IRequest<Result>;
