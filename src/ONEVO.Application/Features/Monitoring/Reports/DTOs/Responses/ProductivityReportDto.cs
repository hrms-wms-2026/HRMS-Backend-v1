namespace ONEVO.Application.Features.Monitoring.Reports.DTOs.Responses;

public record ProductivityReportDto(
    int TotalActiveMinutes,
    int TotalIdleMinutes,
    int TotalMeetingMinutes,
    int ProductiveAppMinutes,
    int PersonalAppMinutes,
    int UnknownAppMinutes,
    decimal AverageActivityScore,
    int TotalWorkedMinutes,
    int TotalBreakMinutes,
    int DayCount);
