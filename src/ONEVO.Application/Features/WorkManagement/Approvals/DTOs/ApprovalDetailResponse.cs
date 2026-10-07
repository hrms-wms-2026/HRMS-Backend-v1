namespace ONEVO.Application.Features.WorkManagement.Approvals.DTOs;

/// <summary>One row of the explanation card's Changes table.</summary>
public sealed record ApprovalFieldResponse(
    string Key,        // property name exactly as stored in PayloadJson (e.g. "Title" or "title") - the frontend overwrites this key
    string Label,
    string Kind,       // "text" | "longtext" | "number" | "hours" | "date" | "priority" | "employee" | "sprint"
    string? Current,   // display-ready string, null = none
    string? Requested,
    string? Applied,   // from AppliedPayloadJson; null when not decided or not edited
    bool Changed,      // Requested != Current
    bool Editable);

public sealed record ApprovalInvitationResponse(
    Guid InviteeId, string InviteeName, string InviteType, Guid InvitedById, string InvitedByName,
    Guid ModuleId, string ModuleTitle, DateTimeOffset? ExpiresAt);

public sealed record ApprovalDetailResponse(
    ApprovalFeedItemResponse Item,
    string? RequestedPayloadJson,
    string? AppliedPayloadJson,
    IReadOnlyList<ApprovalFieldResponse> Fields,
    bool CanEditPayload,
    string? Note,                       // the payload's reason/note, if any
    decimal? CurrentAllocatedHours,     // allocation rows only
    ApprovalInvitationResponse? Invitation,
    bool CanRevert,                     // engine rows only; always false for an invitation row
    DateTimeOffset? RevertableUntil);
