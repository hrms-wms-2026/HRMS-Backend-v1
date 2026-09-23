namespace ONEVO.Application.Features.Leave.Request.Options;

public sealed class LeaveRequestOptions
{
    public const string SectionName = "Leave:Requests";

    public bool AllowBackdatedRequests { get; init; }

    public bool AllowUnpaidSplitWhenBalanceShort { get; init; }

    public bool RequireReason { get; init; }

    public int MaximumRequestRangeDays { get; init; }
}
