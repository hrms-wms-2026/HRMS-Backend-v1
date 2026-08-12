namespace ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

/// <summary>One row of the onboarding position-access approval queue. Only fields an approver
/// needs to review and decide are included - no raw invitation token, no password/session
/// fields, and nothing from access grant requests outside the onboarding action type.</summary>
public sealed record OnboardingAccessGrantRequestListItemResponse(
    Guid AccessGrantRequestId,
    Guid OnboardingDraftId,
    string Status,
    DateTimeOffset RequestedAt,
    Guid RequestedByUserId,
    string? RequestedByName,
    DateTimeOffset? DecidedAt,
    Guid? DecidedByUserId,
    string? DecidedByName,
    string? DecisionNote,
    Guid LegalEntityId,
    string? LegalEntityName,
    Guid? DepartmentId,
    string? DepartmentName,
    Guid TargetPositionId,
    string? TargetPositionName,
    Guid PositionAccessTemplateId,
    Guid RequestedRoleId,
    string? RequestedRoleName,
    string DisplayName,
    string WorkEmail,
    DateOnly StartDate,
    string DraftStatus,
    string? DraftReason,
    string LastSavedStep);

public sealed record OnboardingAccessGrantRequestListPageResponse(
    IReadOnlyList<OnboardingAccessGrantRequestListItemResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);
