namespace ONEVO.Api.Contracts.Leave.Approvals;

public sealed record ApproveLeaveRequestRequest(string? Comment);
public sealed record RejectLeaveRequestRequest(string Reason);

public sealed record ChangeLeaveDecisionRequest(string Decision, string? Comment);
