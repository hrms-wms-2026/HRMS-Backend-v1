using ONEVO.Application.Features.DevPlatform.Subscription.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Billing;

public sealed class SubscriptionStatusRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("active", true)]
    [InlineData("trialing", true)]
    [InlineData("past_due", false)]
    [InlineData("cancelled", false)]
    public void IsActiveStatus_MatchesEntitlementStatuses(string status, bool expected) =>
        Assert.Equal(expected, SubscriptionStatusRules.IsActiveStatus(status));

    [Fact]
    public void IsInTrial_TrueWhenTrialingBeforeTrialEnd()
    {
        var trialEnd = Now.AddDays(5);
        Assert.True(SubscriptionStatusRules.IsInTrial("trialing", trialEnd, Now));
    }

    [Fact]
    public void IsInTrial_FalseWhenTrialExpired()
    {
        var trialEnd = Now.AddDays(-1);
        Assert.False(SubscriptionStatusRules.IsInTrial("trialing", trialEnd, Now));
    }

    [Fact]
    public void IsPastDue_OnlyForPastDueStatus()
    {
        Assert.True(SubscriptionStatusRules.IsPastDue("past_due"));
        Assert.False(SubscriptionStatusRules.IsPastDue("active"));
    }

    [Fact]
    public void IsInGracePeriod_TrueForPastDueWithFutureAccessEndsAt()
    {
        var accessEndsAt = Now.AddDays(3);
        Assert.True(SubscriptionStatusRules.IsInGracePeriod("past_due", accessEndsAt, Now));
    }

    [Fact]
    public void HasActiveAccess_TrueForActiveStatusWithoutAccessEndsAt()
    {
        Assert.True(SubscriptionStatusRules.HasActiveAccess("active", null, Now));
    }

    [Fact]
    public void HasActiveAccess_FalseWhenAccessEndsAtElapsed()
    {
        Assert.False(SubscriptionStatusRules.HasActiveAccess("active", Now.AddDays(-1), Now));
    }

    [Fact]
    public void HasActiveAccess_TrueForPastDueDuringGrace()
    {
        Assert.True(SubscriptionStatusRules.HasActiveAccess("past_due", Now.AddDays(2), Now));
    }

    [Fact]
    public void DaysUntilRenewal_ReturnsNonNegativeDays()
    {
        var periodEnd = DateOnly.FromDateTime(Now.UtcDateTime.AddDays(10));
        Assert.Equal(10, SubscriptionStatusRules.DaysUntilRenewal(periodEnd, Now));
    }

    [Fact]
    public void DaysUntilAccessEnds_ReturnsNullWhenUnset()
    {
        Assert.Null(SubscriptionStatusRules.DaysUntilAccessEnds(null, Now));
    }
}
