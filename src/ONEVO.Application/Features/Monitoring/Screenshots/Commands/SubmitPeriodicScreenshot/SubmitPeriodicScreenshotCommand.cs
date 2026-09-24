using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Monitoring.Screenshots.Commands.SubmitPeriodicScreenshot;

/// <summary>
/// Records one screenshot captured autonomously by the tray's 5-minute collector
/// (no admin-issued AgentCommand behind it — see MonitoringEvidenceAsset.TriggerType).
/// </summary>
public record SubmitPeriodicScreenshotCommand(
    string FileName,
    string ContentType,
    Stream Content,
    DateTimeOffset CapturedAt) : IRequest<Result<Guid>>;
