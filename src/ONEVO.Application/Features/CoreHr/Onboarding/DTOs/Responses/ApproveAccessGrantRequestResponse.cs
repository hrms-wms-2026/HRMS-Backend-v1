namespace ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

public sealed record ApproveAccessGrantRequestResponse(
    Guid AccessGrantRequestId,
    Guid OnboardingDraftId,
    Guid EmployeeId,
    string FinalizationStatus,
    bool InvitationQueued,
    int ChecklistTaskCount,
    string PositionApprovalStatus,
    string MessageKey);
