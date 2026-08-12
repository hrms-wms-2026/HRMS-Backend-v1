namespace ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

public sealed record RejectAccessGrantRequestResponse(
    Guid AccessGrantRequestId,
    Guid OnboardingDraftId,
    string RequestStatus,
    string DraftStatus,
    string? DraftReason,
    string MessageKey);
