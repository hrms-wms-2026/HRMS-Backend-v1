namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.DTOs.Responses;

public sealed record FocusStatusResponse(
    bool IsBreakReminderDue,
    int ContinuousFocusMinutes);
