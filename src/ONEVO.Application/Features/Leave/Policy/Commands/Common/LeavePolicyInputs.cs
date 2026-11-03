namespace ONEVO.Application.Features.Leave.Policy.Commands.Common;

/// <summary>Every rule of one policy version. Amounts are hours; notice is days.</summary>
public sealed record LeavePolicyRulesInput(
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
    IReadOnlyList<LeavePolicyBlackoutInput> Blackouts);

public sealed record LeavePolicyBlackoutInput(DateOnly StartDate, DateOnly EndDate, string? Reason);

public sealed record LeavePolicyAssignmentInput(string TargetKind, Guid TargetId);

/// <summary>A target that already has a different open policy for the same leave type.</summary>
public sealed record LeavePolicyAssignmentConflict(
    string TargetKind,
    Guid TargetId,
    string TargetName,
    Guid ExistingPolicyId,
    string ExistingPolicyName);

/// <summary>Outcome of a write that assigns targets: either the policy id, or the conflicts that
/// need the caller's confirmation (resend with ReplaceConflicts = true). Nothing is written when
/// conflicts are returned.</summary>
public sealed record LeavePolicyWriteResult(Guid? PolicyId, IReadOnlyList<LeavePolicyAssignmentConflict> Conflicts)
{
    public const string ConflictCode = "leave_policy.assignment_conflict";

    public static LeavePolicyWriteResult Written(Guid policyId) => new(policyId, []);
    public static LeavePolicyWriteResult NeedsConfirmation(IReadOnlyList<LeavePolicyAssignmentConflict> conflicts) => new(null, conflicts);
}
