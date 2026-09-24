namespace ONEVO.Application.Features.Monitoring.ClientLogs.DTOs.Requests;

public sealed record RecordClientLogRequest(
    string Level,
    string Message,
    IReadOnlyDictionary<string, object?>? Context,
    DateTimeOffset Timestamp);
