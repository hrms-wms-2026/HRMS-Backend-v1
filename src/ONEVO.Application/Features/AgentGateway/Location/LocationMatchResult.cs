namespace ONEVO.Application.Features.AgentGateway.Location;

public sealed record LocationMatchResult(
    bool IsValid,
    bool IsMatch,
    decimal? DistanceMeters,
    string FailureCode);
