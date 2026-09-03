namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public sealed record ActivityTimelineSegmentDto(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Type);

public sealed record ActivityTimelineDto(
    DateOnly Date,
    IReadOnlyList<ActivityTimelineSegmentDto> Segments);
