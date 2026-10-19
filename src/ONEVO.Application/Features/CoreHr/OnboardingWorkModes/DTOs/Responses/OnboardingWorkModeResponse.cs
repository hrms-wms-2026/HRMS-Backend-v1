namespace ONEVO.Application.Features.CoreHr.OnboardingWorkModes.DTOs.Responses;

public record OnboardingWorkModeResponse(
    Guid Id,
    string Label,
    bool PhotoRequired = false,
    bool SelfRegistersLocation = false,
    bool AllowsDailyLocationChoice = false);
