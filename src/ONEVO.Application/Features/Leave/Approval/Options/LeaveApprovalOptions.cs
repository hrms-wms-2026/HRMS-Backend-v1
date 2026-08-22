namespace ONEVO.Application.Features.Leave.Approval.Options;

public sealed class LeaveApprovalOptions
{
    public const string SectionName = "Leave:Approvals";

    public bool AllowSelfApproval { get; init; }
}
