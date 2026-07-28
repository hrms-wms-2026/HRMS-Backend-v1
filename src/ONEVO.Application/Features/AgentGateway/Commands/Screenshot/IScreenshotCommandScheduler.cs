using ONEVO.Domain.Features.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.Screenshot;

public interface IScreenshotCommandScheduler
{
    Task<bool> TryScheduleAsync(
        RegisteredAgent agent,
        ActivitySnapshot snapshot,
        CancellationToken ct);
}

