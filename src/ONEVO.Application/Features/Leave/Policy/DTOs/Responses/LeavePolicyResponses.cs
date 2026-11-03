namespace ONEVO.Application.Features.Leave.Policy.DTOs.Responses;

public sealed record LeavePolicyListItem(
    Guid Id,
    string Name,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeColor,
    decimal AmountHours,
    string AmountPeriod,
    int CurrentVersionNumber,
    DateOnly? NextVersionEffectiveFrom,
    int AssignmentCount,
    IReadOnlyList<string> AssignedToSummary,
    bool IsActive);

public sealed record LeavePolicyResponse(
    Guid Id,
    string Name,
    string? Description,
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeColor,
    bool IsActive,
    LeavePolicyVersionResponse Current,
    IReadOnlyList<LeavePolicyVersionResponse> Versions,
    IReadOnlyList<LeavePolicyAssignmentResponse> Assignments);

public sealed record LeavePolicyVersionResponse(
    Guid Id,
    int VersionNumber,
    DateOnly EffectiveFrom,
    bool IsCurrent,
    bool IsFuture,
    decimal AmountHours,
    string AmountPeriod,
    decimal? CarryForwardCapHours,
    int? CarryForwardExpiryMonths,
    string EligibleFrom,
    int? EligibleAfterMonths,
    int MinimumNoticeDays,
    decimal MinRequestHours,
    decimal? MaxRequestHours,
    decimal? MaxTeamAbsencePercent,
    string ApprovalMode,
    IReadOnlyList<LeavePolicyBlackoutResponse> Blackouts,
    DateTimeOffset CreatedAt,
    string? CreatedByName);

public sealed record LeavePolicyBlackoutResponse(DateOnly StartDate, DateOnly EndDate, string? Reason);

public sealed record LeavePolicyAssignmentResponse(
    Guid Id, string TargetKind, Guid TargetId, string TargetName, DateOnly EffectiveFrom);

/// <summary>One row per active leave type on the employee screen: the policy that applies today and
/// where it comes from, or no policy.</summary>
public sealed record EmployeeLeavePolicyRow(
    Guid LeaveTypeId,
    string LeaveTypeName,
    string LeaveTypeColor,
    Guid? PolicyId,
    string? PolicyName,
    string? SourceKind,
    string? SourceName,
    decimal? AmountHours,
    string? AmountPeriod,
    bool IsOverride);
