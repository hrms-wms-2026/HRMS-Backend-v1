namespace ONEVO.Application.Features.ActivityMonitoring.DTOs.Responses;

public record EmployeeActivityDailySummaryDto(
    Guid EmployeeId,
    DateOnly Date,
    int TotalActiveMinutes,
    int TotalIdleMinutes,
    int TotalMeetingMinutes,
    decimal ActivePercentage,
    decimal ActivityScore,
    IReadOnlyList<EmployeeConsentNoticeDto> ConsentNotices);
