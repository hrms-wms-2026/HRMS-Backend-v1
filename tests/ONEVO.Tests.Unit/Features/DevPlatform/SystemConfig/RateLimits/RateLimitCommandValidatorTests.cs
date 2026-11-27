using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.Options;
using NSubstitute;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemovePlanRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.RemoveTenantRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetGlobalRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetPlanRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Commands.SetTenantRateLimit;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Queries.GetRateLimitChanges;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.RateLimits;

/// <summary>Improvements task 47: the FluentValidation rules of the rate limit admin commands.</summary>
public sealed class RateLimitCommandValidatorTests
{
    private const string Reason = "Customer import week, approved by ops.";

    private static readonly IOptionsMonitor<RateLimitSettings> AppSettings = Options(new RateLimitSettings());

    private static IOptionsMonitor<RateLimitSettings> Options(RateLimitSettings settings)
    {
        var monitor = Substitute.For<IOptionsMonitor<RateLimitSettings>>();
        monitor.CurrentValue.Returns(settings);
        return monitor;
    }

    private static readonly SetGlobalRateLimitCommandValidator Global = new(AppSettings);
    private static readonly SetPlanRateLimitCommandValidator Plan = new(AppSettings);
    private static readonly SetTenantRateLimitCommandValidator Tenant = new(AppSettings);

    private static SetGlobalRateLimitCommand GlobalCommand(RateLimitName name, int perMinute, int? burst, string reason = Reason) =>
        new(name, perMinute, burst, reason, null, null);

    private static int? ValidBurst(RateLimitName name) => name.Bounds().MinBurst;

    public static TheoryData<RateLimitName> AllLimits() => new(RateLimitNames.All);

    public static TheoryData<RateLimitName> BucketLimits() =>
        new(RateLimitNames.All.Where(n => !n.IsSlidingWindow()));

    // ------------------------------------------------------------- limit names

    [Fact]
    public void Enum_names_are_exactly_the_stored_limit_names()
    {
        RateLimitNames.All.Select(n => n.ToStoredName())
            .Should().Equal(RateLimitSettingRules.LimitNames);
        RateLimitBounds.ByLimit.Keys.Should().BeEquivalentTo(RateLimitSettingRules.LimitNames);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("Enabled")]
    [InlineData("PreAuthentication")]
    [InlineData("Mfa")]
    [InlineData("PasswordReset")]
    [InlineData("7")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Unknown_limit_names_do_not_parse(string? value) =>
        RateLimitNames.TryParse(value, out _).Should().BeFalse();

    [Theory]
    [InlineData("SignedIn", RateLimitName.SignedIn)]
    [InlineData("signedin", RateLimitName.SignedIn)]
    [InlineData("TENANTCEILING", RateLimitName.TenantCeiling)]
    [InlineData("AdminAnonymous", RateLimitName.AdminAnonymous)]
    public void Known_limit_names_parse_by_name(string value, RateLimitName expected)
    {
        RateLimitNames.TryParse(value, out var parsed).Should().BeTrue();
        parsed.Should().Be(expected);
    }

    // ------------------------------------------------------------- per minute bounds

    [Theory]
    [MemberData(nameof(AllLimits))]
    public void PerMinute_floor_and_ceiling_are_accepted(RateLimitName name)
    {
        var bounds = name.Bounds();
        Global.Validate(GlobalCommand(name, bounds.MinPerMinute, ValidBurst(name))).IsValid.Should().BeTrue();
        Global.Validate(GlobalCommand(name, bounds.MaxPerMinute, ValidBurst(name))).IsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllLimits))]
    public void PerMinute_zero_negative_below_floor_and_above_ceiling_are_rejected(RateLimitName name)
    {
        var bounds = name.Bounds();
        foreach (var perMinute in new[] { 0, -60, bounds.MinPerMinute - 60, bounds.MinPerMinute - 1, bounds.MaxPerMinute + 60, bounds.MaxPerMinute + 1 })
        {
            var result = Global.Validate(GlobalCommand(name, perMinute, ValidBurst(name)));
            result.IsValid.Should().BeFalse($"{name} perMinute {perMinute} is outside {bounds.MinPerMinute}-{bounds.MaxPerMinute}");
            result.Errors.Should().Contain(e => e.PropertyName == "PerMinute");
        }
    }

    // ------------------------------------------------------------- burst bounds

    [Theory]
    [MemberData(nameof(BucketLimits))]
    public void Burst_floor_and_ceiling_are_accepted(RateLimitName name)
    {
        var bounds = name.Bounds();
        Global.Validate(GlobalCommand(name, bounds.MinPerMinute, bounds.MinBurst)).IsValid.Should().BeTrue();
        Global.Validate(GlobalCommand(name, bounds.MinPerMinute, bounds.MaxBurst)).IsValid.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BucketLimits))]
    public void Burst_zero_negative_below_floor_and_above_ceiling_are_rejected(RateLimitName name)
    {
        var bounds = name.Bounds();
        foreach (var burst in new[] { 0, -1, bounds.MinBurst!.Value - 1, bounds.MaxBurst!.Value + 1 })
        {
            var result = Global.Validate(GlobalCommand(name, bounds.MinPerMinute, burst));
            result.IsValid.Should().BeFalse($"{name} burst {burst} is outside {bounds.MinBurst}-{bounds.MaxBurst}");
            result.Errors.Should().Contain(e => e.PropertyName == "Burst");
        }
    }

    [Theory]
    [MemberData(nameof(BucketLimits))]
    public void Token_bucket_limit_without_burst_is_rejected(RateLimitName name) =>
        Global.Validate(GlobalCommand(name, name.Bounds().MinPerMinute, null)).Errors
            .Should().Contain(e => e.PropertyName == "Burst");

    [Theory]
    [InlineData(RateLimitName.Anonymous)]
    [InlineData(RateLimitName.AdminAnonymous)]
    public void Sliding_window_limit_with_burst_is_rejected(RateLimitName name) =>
        Global.Validate(GlobalCommand(name, name.Bounds().MinPerMinute, 10)).Errors
            .Should().Contain(e => e.PropertyName == "Burst");

    [Theory]
    [InlineData(RateLimitName.SignedIn, 610)]
    [InlineData(RateLimitName.TrayDevice, 301)]
    [InlineData(RateLimitName.TenantCeiling, 12_030)]
    [InlineData(RateLimitName.AdminSignedIn, 150)]
    public void Token_bucket_perMinute_that_does_not_divide_exactly_is_rejected(RateLimitName name, int perMinute) =>
        Global.Validate(GlobalCommand(name, perMinute, ValidBurst(name))).Errors
            .Should().Contain(e => e.PropertyName == "PerMinute" && e.ErrorMessage.Contains("divide exactly"));

    [Fact]
    public void Divisibility_follows_the_appsettings_refill_period()
    {
        // With a 2-second refill period, 630/min is 21 tokens per period: exact.
        var settings = new RateLimitSettings();
        settings.SignedIn.ReplenishmentPeriodSeconds = 2;
        var validator = new SetGlobalRateLimitCommandValidator(Options(settings));

        validator.Validate(GlobalCommand(RateLimitName.SignedIn, 630, 100)).IsValid.Should().BeTrue();
        validator.Validate(GlobalCommand(RateLimitName.SignedIn, 615, 100)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Sliding_window_perMinute_need_not_be_a_multiple_of_60() =>
        Global.Validate(GlobalCommand(RateLimitName.Anonymous, 301, null)).IsValid.Should().BeTrue();

    // ------------------------------------------------------------- scope rules

    [Theory]
    [InlineData(RateLimitName.Anonymous, null)]
    [InlineData(RateLimitName.AdminAnonymous, null)]
    [InlineData(RateLimitName.AdminSignedIn, 60)]
    public void Platform_wide_limits_are_rejected_on_plan_and_tenant(RateLimitName name, int? burst)
    {
        var perMinute = name.Bounds().MinPerMinute;
        Plan.Validate(new SetPlanRateLimitCommand(Guid.NewGuid(), name, perMinute, burst, Reason, null, null))
            .Errors.Should().Contain(e => e.PropertyName == "LimitName");
        Tenant.Validate(new SetTenantRateLimitCommand(Guid.NewGuid(), name, perMinute, burst, Reason, null, null))
            .Errors.Should().Contain(e => e.PropertyName == "LimitName");
        new RemovePlanRateLimitCommandValidator().Validate(new RemovePlanRateLimitCommand(Guid.NewGuid(), name, Reason, null, null))
            .Errors.Should().Contain(e => e.PropertyName == "LimitName");
        new RemoveTenantRateLimitCommandValidator().Validate(new RemoveTenantRateLimitCommand(Guid.NewGuid(), name, Reason, null, null))
            .Errors.Should().Contain(e => e.PropertyName == "LimitName");
    }

    [Theory]
    [InlineData(RateLimitName.SignedIn)]
    [InlineData(RateLimitName.TrayDevice)]
    [InlineData(RateLimitName.TenantCeiling)]
    public void Per_tenant_limits_are_accepted_on_plan_and_tenant(RateLimitName name)
    {
        var perMinute = name.Bounds().MinPerMinute;
        Plan.Validate(new SetPlanRateLimitCommand(Guid.NewGuid(), name, perMinute, ValidBurst(name), Reason, null, null))
            .IsValid.Should().BeTrue();
        Tenant.Validate(new SetTenantRateLimitCommand(Guid.NewGuid(), name, perMinute, ValidBurst(name), Reason, null, null))
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void Undefined_enum_value_is_rejected() =>
        Global.Validate(GlobalCommand((RateLimitName)42, 600, 100)).Errors
            .Should().Contain(e => e.PropertyName == "LimitName");

    [Fact]
    public void Empty_plan_or_tenant_id_is_rejected()
    {
        Plan.Validate(new SetPlanRateLimitCommand(Guid.Empty, RateLimitName.SignedIn, 600, 100, Reason, null, null))
            .Errors.Should().Contain(e => e.PropertyName == "SubscriptionPlanId");
        Tenant.Validate(new SetTenantRateLimitCommand(Guid.Empty, RateLimitName.SignedIn, 600, 100, Reason, null, null))
            .Errors.Should().Contain(e => e.PropertyName == "TenantId");
    }

    // ------------------------------------------------------------- reason

    [Theory]
    [InlineData("")]
    [InlineData("too short")]
    [InlineData("          short     ")]
    [InlineData("<b>Customer import</b> week")]
    [InlineData("Import week <script>alert(1)</script>")]
    [InlineData("Import week <img src=x onerror=alert(1)>")]
    public void Bad_reason_is_rejected(string reason) =>
        Global.Validate(GlobalCommand(RateLimitName.SignedIn, 600, 100, reason)).Errors
            .Should().Contain(e => e.PropertyName == "Reason");

    [Fact]
    public void Reason_longer_than_500_is_rejected_and_500_is_accepted()
    {
        Global.Validate(GlobalCommand(RateLimitName.SignedIn, 600, 100, new string('a', 501))).IsValid.Should().BeFalse();
        Global.Validate(GlobalCommand(RateLimitName.SignedIn, 600, 100, new string('a', 500))).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Plain_text_reason_with_a_less_than_sign_and_numbers_is_accepted() =>
        Global.Validate(GlobalCommand(RateLimitName.SignedIn, 600, 100, "Raise because p95 < 200 ms for 3 days"))
            .IsValid.Should().BeTrue();

    // ------------------------------------------------------------- history paging

    [Theory]
    [InlineData("global", 1, 50, true)]
    [InlineData(null, 1, 25, true)]
    [InlineData("everyone", 1, 25, false)]
    [InlineData("tenant", 0, 25, false)]
    [InlineData("tenant", 1, 51, false)]
    [InlineData("tenant", 1, 0, false)]
    public void History_query_checks_scope_and_page_size(string? scope, int page, int pageSize, bool valid) =>
        new GetRateLimitChangesQueryValidator()
            .Validate(new GetRateLimitChangesQuery(scope, null, null, page, pageSize))
            .IsValid.Should().Be(valid);
}
