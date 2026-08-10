using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Policy.Queries.GetEffectiveTrayPolicy;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring.Policy;

public class GetEffectiveTrayPolicyQueryHandlerTests
{
    private readonly FakeMonitoringToggleResolver _toggles = new();
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly FixedDateTimeProvider _clock = new(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public GetEffectiveTrayPolicyQueryHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId,
                Name = "Test",
                Slug = "test",
                Status = TenantStatus.Active
            });
    }

    private GetEffectiveTrayPolicyQueryHandler CreateSut() => new(
        _toggles,
        _device.Object,
        _tenants.Object,
        _switcher.Object,
        _clock);

    [Fact]
    public async Task Screenshot_prompt_requires_activity_capture_and_auto_capture()
    {
        _toggles.Set(MonitoringCapability.ActivityMonitoring, true);
        _toggles.Set(MonitoringCapability.ScreenshotCapture, true);
        _toggles.Set(MonitoringCapability.AutoScreenshotCapture, false);

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActivitySignalEnabled.Should().BeTrue();
        result.Value.InactivityScreenshotEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Inactivity_screenshot_enabled_when_all_three_capabilities_on()
    {
        _toggles.Set(MonitoringCapability.ActivityMonitoring, true);
        _toggles.Set(MonitoringCapability.ScreenshotCapture, true);
        _toggles.Set(MonitoringCapability.AutoScreenshotCapture, true);

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.InactivityScreenshotEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task All_toggles_unset_yields_all_flags_false_safe_default()
    {
        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActivitySignalEnabled.Should().BeFalse();
        result.Value.AppUsageEnabled.Should().BeFalse();
        result.Value.ScreenshotEnabled.Should().BeFalse();
        result.Value.InactivityScreenshotEnabled.Should().BeFalse();
        result.Value.CameraVerificationEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Unknown_tenant_returns_401()
    {
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ValidUntil_is_exactly_one_hour_after_the_injected_clock()
    {
        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ValidUntil.Should().Be(_clock.UtcNow.AddHours(1));
    }

    [Fact]
    public async Task Version_is_stable_for_identical_toggles_and_changes_when_a_toggle_changes()
    {
        _toggles.Set(MonitoringCapability.ActivityMonitoring, true);
        var first = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);
        var second = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        first.Value!.Version.Should().Be(second.Value!.Version);

        _toggles.Set(MonitoringCapability.ApplicationTracking, true);
        var third = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        third.Value!.Version.Should().NotBe(first.Value.Version);
    }

    [Fact]
    public async Task Tenant_context_is_switched_before_resolving_toggles()
    {
        await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        _switcher.Verify(s => s.SwitchToTenantAsync(
                It.Is<TenantRegistryEntry>(e => e.TenantId == _tenantId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
