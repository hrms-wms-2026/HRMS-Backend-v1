namespace ONEVO.Application.Features.CoreHr.OnboardingDrafts.DTOs.Responses;

public sealed record FinalizeOnboardingDraftResponse(
    Guid DraftId,
    Guid? EmployeeId,
    string Status,
    string? DraftReason,
    bool InvitationQueued,
    bool PositionApprovalPending,
    bool ChecklistTasksCreated,
    string MessageKey);
