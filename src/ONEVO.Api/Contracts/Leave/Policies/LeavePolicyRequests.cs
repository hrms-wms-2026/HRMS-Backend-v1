using ONEVO.Application.Features.Leave.Policy.Commands.Common;

namespace ONEVO.Api.Contracts.Leave.Policies;

public sealed record LeavePolicyRulesRequest(
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
    IReadOnlyList<LeavePolicyBlackoutRequest>? Blackouts)
{
    public LeavePolicyRulesInput ToInput() => new(
        AmountHours, AmountPeriod, CarryForwardCapHours, CarryForwardExpiryMonths, EligibleFrom, EligibleAfterMonths,
        MinimumNoticeDays, MinRequestHours, MaxRequestHours, MaxTeamAbsencePercent, ApprovalMode,
        (Blackouts ?? []).Select(b => new LeavePolicyBlackoutInput(b.StartDate, b.EndDate, b.Reason)).ToList());
}

public sealed record LeavePolicyBlackoutRequest(DateOnly StartDate, DateOnly EndDate, string? Reason);

public sealed record LeavePolicyAssignmentRequest(string TargetKind, Guid TargetId);

public sealed record CreateLeavePolicyRequest(
    Guid LeaveTypeId,
    string Name,
    string? Description,
    LeavePolicyRulesRequest Rules,
    IReadOnlyList<LeavePolicyAssignmentRequest>? Assignments,
    bool ReplaceConflicts);

/// <summary>Version: task 42, the policy's row version from the last read; a stale one returns 409.</summary>
public sealed record UpdateLeavePolicyDetailsRequest(string Name, string? Description, string? Version = null);

public sealed record AddLeavePolicyVersionRequest(DateOnly ApplyFrom, LeavePolicyRulesRequest Rules);

public sealed record SetLeavePolicyAssignmentsRequest(
    IReadOnlyList<LeavePolicyAssignmentRequest>? Assignments, bool ReplaceConflicts);

public sealed record CloneLeavePolicyRequest(string Name);

public sealed record SetEmployeeLeavePolicyRequest(Guid PolicyId);
