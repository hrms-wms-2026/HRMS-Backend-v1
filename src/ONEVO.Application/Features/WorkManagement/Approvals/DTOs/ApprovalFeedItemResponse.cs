namespace ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

public static class ApprovalFeedSources
{
    public const string Engine = "engine";
    public const string Invitation = "invitation";
}

/// <summary>One row of the Approvals page. Direction is from the caller's side. Status uses the engine
/// vocabulary (pending/approved/rejected/cancelled/stale) - invitations are mapped onto it.</summary>
public sealed record ApprovalFeedItemResponse(
    Guid Id,
    string Source,
    string ActionType,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    Guid? ModuleId,
    string? ModuleTitle,
    string ProjectName,
    string Status,
    string Direction,
    Guid RequestedById,
    string RequestedByName,
    Guid ApproverId,
    string ApproverName,
    Guid? DecidedById,
    string? DecidedByName,
    string? DecisionComment,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    bool CanDecide,
    bool CanCancel,
    int CommentCount,
    string? Summary);
