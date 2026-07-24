namespace ONEVO.Application.Features.AgentGateway.Location;

public sealed record LocationCapture(
    decimal Latitude,
    decimal Longitude,
    decimal AccuracyMeters,
    DateTimeOffset CapturedAt,
    string PermissionState);

public sealed record LocationTarget(
    Guid SourceId,
    string Source,
    decimal Latitude,
    decimal Longitude,
    int AllowedRadiusMeters);
