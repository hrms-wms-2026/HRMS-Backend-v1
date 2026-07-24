namespace ONEVO.Application.Features.AgentGateway.Location;

public interface ILocationVerificationService
{
    LocationMatchResult ValidateCapture(
        LocationCapture capture,
        DateTimeOffset serverNow);

    LocationMatchResult Evaluate(
        LocationCapture capture,
        LocationTarget target,
        DateTimeOffset serverNow);
}
