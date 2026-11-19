namespace ONEVO.Application.Features.Leave.Balance.DTOs.Responses;

public sealed record LeaveWorkWindowResponse(
    TimeOnly? WorkStartTime,
    TimeOnly? WorkEndTime,
    int? BreakDurationMinutes,
    string? Timezone);
