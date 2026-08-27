namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public sealed record WorkPatternDayDto(
    DateOnly Date,
    int FocusMinutes,
    int MeetingMinutes,
    int AdminMinutes);

public sealed record WorkPatternResponse(IReadOnlyList<WorkPatternDayDto> Days);
