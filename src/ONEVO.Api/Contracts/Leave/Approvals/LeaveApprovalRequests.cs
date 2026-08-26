namespace ONEVO.Api.Contracts.Leave.Approvals;

public sealed record ApproveLeaveRequestRequest(string? Comment);
public sealed record RejectLeaveRequestRequest(string Reason);
public sealed record RequestLeaveInformationRequest(string Question);
public sealed record RespondLeaveInformationRequest(string Message, IReadOnlyList<Guid>? FileRecordIds);
public sealed record BulkApproveLeaveRequestsRequest(IReadOnlyList<Guid> RequestIds, string? Comment);
public sealed record BulkRejectLeaveRequestsRequest(IReadOnlyList<Guid> RequestIds, string Reason);
