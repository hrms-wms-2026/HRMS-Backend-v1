namespace ONEVO.Application.Features.Leave.Entitlement.Options;

public class LeaveEntitlementYearOptions
{
    public const string SectionName = "Leave:Entitlements:Years";
    public int MinimumYear { get; init; }
    public int MaximumYear { get; init; }
}
