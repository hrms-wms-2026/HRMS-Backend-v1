namespace ONEVO.Application.Features.AgentGateway.Location;

public interface ILocationVerificationService
{
    LocationMatchResult Evaluate(
        LocationCapture capture,
        LocationTarget target,
        DateTimeOffset serverNow);
}
