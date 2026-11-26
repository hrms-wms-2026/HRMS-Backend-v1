using FluentAssertions;
using ONEVO.Application.Common.Configuration;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;
using ONEVO.Infrastructure.Persistence.Rls;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.RateLimiting;

public class RateLimitSettingRulesTests
{
    private static Type? LimitType(string name) =>
        typeof(RateLimitSettings).GetProperty(name)?.PropertyType;

    [Fact]
    public void Every_limit_name_is_a_RateLimitSettings_property_as_written()
    {
        foreach (var name in RateLimitSettingRules.LimitNames)
            LimitType(name).Should().NotBeNull($"{name} must map to a RateLimitSettings property without a lookup table");
    }

    [Fact]
    public void PreAuthentication_and_the_master_switch_cannot_be_overridden()
    {
        RateLimitSettingRules.LimitNames.Should().NotContain(nameof(RateLimitSettings.PreAuthentication));
        RateLimitSettingRules.LimitNames.Should().NotContain(nameof(RateLimitSettings.Enabled));
        RateLimitSettingRules.LimitNames.Should().HaveCount(6);
    }

    [Fact]
    public void Sliding_window_names_are_exactly_the_sliding_window_limits()
    {
        RateLimitSettingRules.SlidingWindowLimitNames.Should().BeEquivalentTo(
            RateLimitSettingRules.LimitNames.Where(n => LimitType(n) == typeof(SlidingWindowLimit)));
        RateLimitSettingRules.LimitNames.Except(RateLimitSettingRules.SlidingWindowLimitNames)
            .Should().OnlyContain(n => LimitType(n) == typeof(TokenBucketLimit));
    }

    [Fact]
    public void Plans_and_tenants_can_change_only_the_three_per_tenant_limits()
    {
        RateLimitSettingRules.PerTenantLimitNames.Should().BeEquivalentTo(
            [RateLimitSettingRules.SignedIn, RateLimitSettingRules.TrayDevice, RateLimitSettingRules.TenantCeiling]);
        RateLimitSettingRules.LimitNames.Should().Contain(RateLimitSettingRules.PerTenantLimitNames);
    }

    [Fact]
    public void Scopes_are_global_plan_and_tenant()
    {
        RateLimitSettingRules.Scopes.Should().Equal("global", "plan", "tenant");
    }

    [Theory]
    [InlineData("rate_limit_settings")]
    [InlineData("rate_limit_setting_changes")]
    public void Both_tables_are_under_tenant_or_system_rls(string table)
    {
        RlsTableRegistry.AddedAfterBaseline[table].Should().Be(RlsPredicate.TenantOrSystem);
        RlsTableRegistry.Tables[table].Should().Be(RlsPredicate.TenantOrSystem);
    }
}
