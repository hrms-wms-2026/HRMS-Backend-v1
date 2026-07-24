namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record MeetingSessionDto(
    Guid Id,
    DateTimeOffset MeetingStart,
    DateTimeOffset MeetingEnd,
    string Platform,
    int DurationMinutes,
    bool HadCameraOn,
    bool HadMicActivity);
