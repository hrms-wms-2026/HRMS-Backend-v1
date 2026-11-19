using ONEVO.Application.Features.Leave.Policy.Services;

namespace ONEVO.Application.Features.Leave.Entitlement.Helpers;

/// <summary>When hours carried into <c>year</c> expire: 1 Jan + the policy's expiry months, or never.
/// The policy is the one in force on 1 Jan of that year (the carry was made at that year close).</summary>
public static class LeaveCarryExpiry
{
    public static DateOnly ResolutionDate(int year) => new(year, 1, 1);

    public static DateOnly? Date(int year, int? expiryMonths) =>
        expiryMonths is null or <= 0 ? null : new DateOnly(year, 1, 1).AddMonths(expiryMonths.Value);

    public static DateOnly? For(ResolvedLeavePolicy? policy, int year) =>
        Date(year, policy?.Version.CarryForwardExpiryMonths);
}
