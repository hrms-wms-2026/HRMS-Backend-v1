namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record ActivityDailySummaryDto(
    Guid EmployeeId,
    DateOnly Date,
    int TotalActiveMinutes,
    int TotalIdleMinutes,
    int TotalMeetingMinutes,
    decimal ActivePercentage,
    int ProductiveAppMinutes,
    int PersonalAppMinutes,
    decimal ActivityScore,
    string TopAppsJson,
    decimal IntensityAvg,
    int KeyboardTotal,
    int MouseTotal);
