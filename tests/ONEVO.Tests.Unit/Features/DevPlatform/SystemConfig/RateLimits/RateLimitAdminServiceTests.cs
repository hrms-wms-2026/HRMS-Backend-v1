using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ONEVO.Application.Common.Configuration;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.RateLimits.Services;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting;
using ONEVO.Domain.Features.SharedPlatform.RateLimiting.Entities;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig.RateLimits;

/// <summary>Improvements task 47: writes, history, invalidation, concurrency and the ceiling rule.</summary>
public sealed class RateLimitAdminServiceTests
{
    private const string Reason = "Customer import week, approved by ops.";
    private readonly IRateLimitSettingRepository _repository = Substitute.For<IRateLimitSettingRepository>();
    private readonly IRateLimitSettingsInvalidator _invalidator = Substitute.For<IRateLimitSettingsInvalidator>();
    private readonly ICurrentPlatformUserContext _platformUser = Substitute.For<ICurrentPlatformUserContext>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);
    private readonly List<RateLimitOverrideRecord> _rows = [];
    private readonly List<RateLimitSettingChange> _changes = [];
    private readonly RateLimitAdminService _service;

    public RateLimitAdminServiceTests()
    {
        _platformUser.UserId.Returns(_actor);
        _clock.UtcNow.Returns(_now);
        var options = Substitute.For<IOptionsMonitor<RateLimitSettings>>();
        options.CurrentValue.Returns(new RateLimitSettings());
        _repository.ListForScopeAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_ => _rows.ToList());
        _repository.SetExpectedVersion(Arg.Any<RateLimitSetting>(), Arg.Any<string>()).Returns(true);
        _repository.When(r => r.AddChange(Arg.Any<RateLimitSettingChange>()))
            .Do(c => _changes.Add(c.Arg<RateLimitSettingChange>()));
        _service = new RateLimitAdminService(
            _repository, _invalidator, options, _platformUser, _clock, NullLogger<RateLimitAdminService>.Instance);
    }

    private static RateLimitChangeRequest Set(RateLimitTarget target, RateLimitName name, int perMinute, int? burst, string? rowVersion = null) =>
        new(target, name, new RateLimitValue(perMinute, burst), Reason, rowVersion, "corr-1");

    private static RateLimitChangeRequest Remove(RateLimitTarget target, RateLimitName name, string? rowVersion = "1") =>
        new(target, name, null, Reason, rowVersion, "corr-1");

    private void Existing(RateLimitSetting setting)
    {
        _repository.GetTrackedAsync(setting.Scope, setting.SubscriptionPlanId, setting.TenantId, setting.LimitName, Arg.Any<CancellationToken>())
            .Returns(setting);
        _rows.Add(new RateLimitOverrideRecord(setting.Id, setting.Scope, setting.SubscriptionPlanId, setting.TenantId,
            setting.LimitName, setting.PerMinute, setting.Burst, setting.UpdatedAt, setting.UpdatedByPlatformUserId, "1"));
    }

    private static RateLimitSetting Row(RateLimitTarget target, RateLimitName name, int perMinute, int? burst) => new()
    {
        Id = Guid.NewGuid(),
        Scope = target.Scope,
        SubscriptionPlanId = target.SubscriptionPlanId,
        TenantId = target.TenantId,
        LimitName = name.ToStoredName(),
        PerMinute = perMinute,
        Burst = burst,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        UpdatedByPlatformUserId = Guid.NewGuid(),
    };

    // ------------------------------------------------------------- success and history

    [Fact]
    public async Task New_global_override_writes_one_history_row_then_invalidates()
    {
        var result = await _service.ApplyAsync(Set(RateLimitTarget.Global, RateLimitName.SignedIn, 900, 150), default);

        result.IsSuccess.Should().BeTrue();
        _repository.Received(1).Add(Arg.Is<RateLimitSetting>(s =>
            s.Scope == "global" && s.LimitName == "SignedIn" && s.PerMinute == 900 && s.Burst == 150
            && s.UpdatedByPlatformUserId == _actor && s.UpdatedAt == _now));
        var change = _changes.Should().ContainSingle().Subject;
        change.PlatformUserId.Should().Be(_actor);
        change.OldPerMinute.Should().BeNull();
        change.OldBurst.Should().BeNull();
        change.NewPerMinute.Should().Be(900);
        change.NewBurst.Should().Be(150);
        change.Reason.Should().Be(Reason);
        change.CorrelationId.Should().Be("corr-1");
        Received.InOrder(() =>
        {
            _repository.SaveChangesAsync(Arg.Any<CancellationToken>());
            _invalidator.InvalidateAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Updating_an_override_records_old_and_new_values()
    {
        var tenantId = Guid.NewGuid();
        _repository.GetTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new RateLimitTenantRef(tenantId, null, false));
        var row = Row(RateLimitTarget.Tenant(tenantId), RateLimitName.TrayDevice, 300, 60);
        Existing(row);

        var result = await _service.ApplyAsync(Set(RateLimitTarget.Tenant(tenantId), RateLimitName.TrayDevice, 600, 120, "1"), default);

        result.IsSuccess.Should().BeTrue();
        _repository.Received(1).SetExpectedVersion(row, "1");
        row.PerMinute.Should().Be(600);
        row.Burst.Should().Be(120);
        var change = _changes.Should().ContainSingle().Subject;
        (change.OldPerMinute, change.OldBurst, change.NewPerMinute, change.NewBurst).Should().Be((300, 60, 600, 120));
        change.TenantId.Should().Be(tenantId);
        change.Scope.Should().Be("tenant");
    }

    [Fact]
    public async Task Removing_an_override_records_old_values_and_null_new_values()
    {
        var row = Row(RateLimitTarget.Global, RateLimitName.Anonymous, 600, null);
        Existing(row);

        var result = await _service.ApplyAsync(Remove(RateLimitTarget.Global, RateLimitName.Anonymous), default);

        result.IsSuccess.Should().BeTrue();
        _repository.Received(1).Remove(row);
        var change = _changes.Should().ContainSingle().Subject;
        (change.OldPerMinute, change.NewPerMinute, change.NewBurst).Should().Be((600, (int?)null, (int?)null));
        await _invalidator.Received(1).InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Same_value_is_a_no_op_without_history_or_invalidation()
    {
        Existing(Row(RateLimitTarget.Global, RateLimitName.SignedIn, 900, 150));

        var result = await _service.ApplyAsync(Set(RateLimitTarget.Global, RateLimitName.SignedIn, 900, 150, "stale"), default);

        result.IsSuccess.Should().BeTrue();
        _changes.Should().BeEmpty();
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _invalidator.DidNotReceive().InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_a_missing_override_is_a_no_op()
    {
        var result = await _service.ApplyAsync(Remove(RateLimitTarget.Global, RateLimitName.SignedIn, null), default);

        result.IsSuccess.Should().BeTrue();
        _changes.Should().BeEmpty();
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_invalidation_still_returns_success()
    {
        _invalidator.InvalidateAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _service.ApplyAsync(Set(RateLimitTarget.Global, RateLimitName.SignedIn, 900, 150), default);

        result.IsSuccess.Should().BeTrue();
    }

    // ------------------------------------------------------------- concurrency

    [Fact]
    public async Task Stale_row_version_returns_409()
    {
        Existing(Row(RateLimitTarget.Global, RateLimitName.SignedIn, 900, 150));
        _repository.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConcurrencyConflictException());

        var result = await _service.ApplyAsync(Set(RateLimitTarget.Global, RateLimitName.SignedIn, 1200, 150, "1"), default);

        result.StatusCode.Should().Be(409);
        await _invalidator.DidNotReceive().InvalidateAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Saving_over_an_existing_override_without_row_version_returns_409()
    {
        Existing(Row(RateLimitTarget.Global, RateLimitName.SignedIn, 900, 150));

        var result = await _service.ApplyAsync(Set(RateLimitTarget.Global, RateLimitName.SignedIn, 1200, 150), default);

        result.StatusCode.Should().Be(409);
        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Row_version_for_an_override_that_is_gone_returns_409()
    {
        var result = await _service.ApplyAsync(Set(RateLimitTarget.Global, RateLimitName.SignedIn, 1200, 150, "5"), default);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Two_admins_creating_the_same_override_second_gets_409()
    {
        _repository.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new UniqueConstraintConflictException(new InvalidOperationException("duplicate")));

        var result = await _service.ApplyAsync(Set(RateLimitTarget.Global, RateLimitName.SignedIn, 1200, 150), default);

        result.StatusCode.Should().Be(409);
    }

    // ------------------------------------------------------------- ceiling

    [Fact]
    public async Task Global_ceiling_below_signed_in_is_rejected()
    {
        Existing(Row(RateLimitTarget.Global, RateLimitName.SignedIn, 3000, 500));

        var result = await _service.ApplyAsync(Set(RateLimitTarget.Global, RateLimitName.TenantCeiling, 1200, 500), default);

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("rate_limit_ceiling_below_signed_in");
        _changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Signed_in_above_the_effective_ceiling_is_rejected()
    {
        Existing(Row(RateLimitTarget.Global, RateLimitName.TenantCeiling, 1200, 500));

        var result = await _service.ApplyAsync(Set(RateLimitTarget.Global, RateLimitName.SignedIn, 1260, 100), default);

        result.ErrorCode.Should().Be("rate_limit_ceiling_below_signed_in");
    }

    [Fact]
    public async Task Tenant_signed_in_is_checked_against_the_ceiling_it_inherits_from_its_plan()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        _repository.GetTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new RateLimitTenantRef(tenantId, planId, false));
        Existing(Row(RateLimitTarget.Plan(planId), RateLimitName.TenantCeiling, 1200, 500));

        var tooHigh = await _service.ApplyAsync(Set(RateLimitTarget.Tenant(tenantId), RateLimitName.SignedIn, 3000, 500), default);
        var fits = await _service.ApplyAsync(Set(RateLimitTarget.Tenant(tenantId), RateLimitName.SignedIn, 1200, 500), default);

        tooHigh.ErrorCode.Should().Be("rate_limit_ceiling_below_signed_in");
        fits.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Removing_a_ceiling_that_would_leave_signed_in_above_it_is_rejected()
    {
        var planId = Guid.NewGuid();
        _repository.GetPlanAsync(planId, Arg.Any<CancellationToken>()).Returns(new RateLimitPlanRef(planId, true));
        // Plan raises SignedIn to 3000/500 and its ceiling to 6000/1000. Without the plan ceiling it
        // would fall back to the global ceiling, 1200/500, which is below the plan's SignedIn.
        Existing(Row(RateLimitTarget.Global, RateLimitName.TenantCeiling, 1200, 500));
        Existing(Row(RateLimitTarget.Plan(planId), RateLimitName.SignedIn, 3000, 500));
        Existing(Row(RateLimitTarget.Plan(planId), RateLimitName.TenantCeiling, 6000, 1000));

        var result = await _service.ApplyAsync(Remove(RateLimitTarget.Plan(planId), RateLimitName.TenantCeiling), default);

        result.ErrorCode.Should().Be("rate_limit_ceiling_below_signed_in");
    }

    // ------------------------------------------------------------- targets

    [Theory]
    [InlineData(RateLimitName.Anonymous)]
    [InlineData(RateLimitName.AdminAnonymous)]
    [InlineData(RateLimitName.AdminSignedIn)]
    public async Task Platform_wide_limit_on_a_tenant_is_rejected(RateLimitName name)
    {
        var result = await _service.ApplyAsync(Set(RateLimitTarget.Tenant(Guid.NewGuid()), name, 600, 60), default);

        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("rate_limit_platform_only");
    }

    [Fact]
    public async Task Missing_plan_returns_404_and_archived_plan_returns_400()
    {
        var missing = Guid.NewGuid();
        var archived = Guid.NewGuid();
        _repository.GetPlanAsync(archived, Arg.Any<CancellationToken>()).Returns(new RateLimitPlanRef(archived, false));

        (await _service.ApplyAsync(Set(RateLimitTarget.Plan(missing), RateLimitName.SignedIn, 900, 150), default))
            .StatusCode.Should().Be(404);
        (await _service.ApplyAsync(Set(RateLimitTarget.Plan(archived), RateLimitName.SignedIn, 900, 150), default))
            .ErrorCode.Should().Be("rate_limit_plan_archived");
    }

    [Fact]
    public async Task Missing_tenant_returns_404_and_cancelled_tenant_returns_400()
    {
        var missing = Guid.NewGuid();
        var archived = Guid.NewGuid();
        _repository.GetTenantAsync(archived, Arg.Any<CancellationToken>()).Returns(new RateLimitTenantRef(archived, null, true));

        (await _service.ApplyAsync(Set(RateLimitTarget.Tenant(missing), RateLimitName.SignedIn, 900, 150), default))
            .StatusCode.Should().Be(404);
        (await _service.ApplyAsync(Set(RateLimitTarget.Tenant(archived), RateLimitName.SignedIn, 900, 150), default))
            .ErrorCode.Should().Be("rate_limit_tenant_archived");
    }

    [Fact]
    public async Task No_platform_user_is_forbidden()
    {
        _platformUser.UserId.Returns((Guid?)null);

        var result = await _service.ApplyAsync(Set(RateLimitTarget.Global, RateLimitName.SignedIn, 900, 150), default);

        result.StatusCode.Should().Be(403);
    }

    // ------------------------------------------------------------- reads

    [Fact]
    public async Task Global_view_lists_all_six_limits_with_appsettings_effective_and_bounds()
    {
        Existing(Row(RateLimitTarget.Global, RateLimitName.SignedIn, 900, 150));

        var view = await _service.GetGlobalAsync(default);

        view.Select(v => v.LimitName).Should().Equal(RateLimitSettingRules.LimitNames);
        var signedIn = view.Single(v => v.LimitName == "SignedIn");
        signedIn.AppSettings.Should().Be(new RateLimitValueDto(600, 100));
        signedIn.Effective.Should().Be(new RateLimitValueDto(900, 150));
        signedIn.EffectiveSource.Should().Be("global");
        signedIn.Override!.RowVersion.Should().Be("1");
        signedIn.Bounds.Should().Be(new RateLimitBoundsDto(120, 3_000, 30, 500));
        var anonymous = view.Single(v => v.LimitName == "Anonymous");
        anonymous.Effective.Should().Be(new RateLimitValueDto(300, null));
        anonymous.EffectiveSource.Should().Be("appsettings");
        anonymous.Algorithm.Should().Be("sliding_window");
    }

    [Fact]
    public async Task Tenant_view_shows_where_each_value_comes_from()
    {
        var tenantId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        _repository.GetTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new RateLimitTenantRef(tenantId, planId, false));
        Existing(Row(RateLimitTarget.Tenant(tenantId), RateLimitName.SignedIn, 1200, 200));
        Existing(Row(RateLimitTarget.Plan(planId), RateLimitName.TrayDevice, 600, 120));

        var view = (await _service.GetTenantAsync(tenantId, default)).Value!;

        view.Limits.Select(l => l.LimitName).Should().Equal("SignedIn", "TrayDevice", "TenantCeiling");
        view.Limits.Select(l => l.EffectiveSource).Should().Equal("tenant", "plan", "appsettings");
        view.Limits[0].Effective.Should().Be(new RateLimitValueDto(1200, 200));
    }

    [Fact]
    public async Task Plan_view_shows_the_inherited_global_value()
    {
        var planId = Guid.NewGuid();
        _repository.GetPlanAsync(planId, Arg.Any<CancellationToken>()).Returns(new RateLimitPlanRef(planId, true));
        Existing(Row(RateLimitTarget.Global, RateLimitName.SignedIn, 900, 150));
        Existing(Row(RateLimitTarget.Plan(planId), RateLimitName.SignedIn, 1200, 200));

        var view = (await _service.GetPlanAsync(planId, default)).Value!;

        var signedIn = view.Limits.Single(l => l.LimitName == "SignedIn");
        signedIn.Inherited.Should().Be(new RateLimitValueDto(900, 150));
        signedIn.InheritedSource.Should().Be("global");
        signedIn.Effective.Should().Be(new RateLimitValueDto(1200, 200));
        signedIn.EffectiveSource.Should().Be("plan");
    }
}
