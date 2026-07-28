using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.CompleteScreenshotCommand;

public sealed record CompleteScreenshotCommand(
    Guid AgentId,
    Guid CommandId,
    string FileName,
    string ContentType,
    Stream Content)
    : IRequest<Result<CompleteScreenshotResponse>>;

public sealed record CompleteScreenshotResponse(
    Guid CommandId,
    Guid EvidenceAssetId,
    DateTimeOffset CapturedAt,
    string Status);

