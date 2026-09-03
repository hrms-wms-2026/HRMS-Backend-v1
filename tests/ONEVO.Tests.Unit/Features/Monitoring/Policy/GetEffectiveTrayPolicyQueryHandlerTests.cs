using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Policy.Queries.GetEffectiveTrayPolicy;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring.Policy;

public class GetEffectiveTrayPolicyQueryHandlerTests
{
    private readonly Mock<ITrayCurrentDevice> _device = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantContextSwitcher> _switcher = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<IAttendanceTodayStateService> _todayState = new();
    private readonly FrozenClock _clock = new(new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero));

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public GetEffectiveTrayPolicyQueryHandlerTests()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(true);
        _device.Setup(d => d.TenantId).Returns(_tenantId);
        _device.Setup(d => d.UserId).Returns(_userId);
        _device.Setup(d => d.DeviceRegistrationId).Returns(_deviceId);
        _device.Setup(d => d.LegalEntityId).Returns(_legalEntityId);

        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant
            {
                Id = _tenantId,
                Name = "Test",
                Slug = "test",
                Status = TenantStatus.Active
            });

        _todayState.Setup(t => t.ResolveContextAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.Success(BuildContext(
                allowedMethods: new AllowedClockInMethods(Web: true, DesktopTray: false, Biometric: false, PhotoRequired: false, LocationRequired: false, AllowedRadiusMeters: null))));
    }

    private GetEffectiveTrayPolicyQueryHandler CreateSut() => new(
        _device.Object,
        _tenants.Object,
        _switcher.Object,
        _toggles.Object,
        _clock,
        _todayState.Object);

    private AttendanceTodayContext BuildContext(AllowedClockInMethods allowedMethods) => new(
        new Employee { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId, LegalEntityId = _legalEntityId },
        new LegalEntity { Id = _legalEntityId, TenantId = _tenantId, Timezone = "Asia/Colombo" },
        "Asia/Colombo",
        TimeZoneInfo.Utc,
        DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
        _clock.UtcNow,
        _clock.UtcNow,
        new AttendanceSchedule("configured", true, new(9, 0), new(17, 30), 510),
        "remote",
        "active_employee_work_mode",
        null,
        "configured",
        allowedMethods,
        new AttendanceLocalDayWindow(_clock.UtcNow, _clock.UtcNow.AddDays(1)));

    private void Set(MonitoringCapability capability, bool enabled) =>
        _toggles.Setup(t => t.IsEnabledAsync(
                _tenantId, _userId, _legalEntityId, capability, It.IsAny<CancellationToken>()))
            .ReturnsAsync(enabled);

    private void SetIdleThreshold(int minutes) =>
        _toggles.Setup(t => t.GetIdleThresholdMinutesAsync(
                _tenantId, _userId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(minutes);

    [Fact]
    public async Task Unauthenticated_device_returns_401()
    {
        _device.Setup(d => d.IsAuthenticated).Returns(false);

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Tenant_not_found_returns_401()
    {
        _tenants.Setup(t => t.GetByIdAsync(_tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Screenshot_prompt_requires_activity_capture_and_auto_capture()
    {
        Set(MonitoringCapability.ActivityMonitoring, true);
        Set(MonitoringCapability.ApplicationTracking, true);
        Set(MonitoringCapability.ScreenshotCapture, true);
        Set(MonitoringCapability.AutoScreenshotCapture, false);
        Set(MonitoringCapability.IdentityVerification, true);

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActivitySignalEnabled.Should().BeTrue();
        result.Value.AppUsageEnabled.Should().BeTrue();
        result.Value.ScreenshotEnabled.Should().BeTrue();
        result.Value.InactivityScreenshotEnabled.Should().BeFalse();
        result.Value.CameraVerificationEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task All_screenshot_toggles_on_enables_inactivity_prompt()
    {
        Set(MonitoringCapability.ActivityMonitoring, true);
        Set(MonitoringCapability.ApplicationTracking, true);
        Set(MonitoringCapability.ScreenshotCapture, true);
        Set(MonitoringCapability.AutoScreenshotCapture, true);
        Set(MonitoringCapability.IdentityVerification, false);

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.InactivityScreenshotEnabled.Should().BeTrue();
        result.Value.ScreenshotEnabled.Should().BeTrue();
        result.Value.CameraVerificationEnabled.Should().BeFalse();
        result.Value.ValidUntil.Should().Be(_clock.UtcNow.AddHours(1));
        result.Value.Version.Should().MatchRegex("^[0-9A-F]{16}$");
    }

    [Fact]
    public async Task Resolved_idle_threshold_minutes_flows_into_dto()
    {
        Set(MonitoringCapability.ActivityMonitoring, true);
        Set(MonitoringCapability.ApplicationTracking, true);
        Set(MonitoringCapability.ScreenshotCapture, true);
        Set(MonitoringCapability.AutoScreenshotCapture, true);
        Set(MonitoringCapability.IdentityVerification, false);
        SetIdleThreshold(7);

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IdleThresholdMinutes.Should().Be(7);
    }

    [Fact]
    public async Task Switches_into_jwt_tenant_before_resolving_toggles()
    {
        Set(MonitoringCapability.ActivityMonitoring, false);
        Set(MonitoringCapability.ApplicationTracking, false);
        Set(MonitoringCapability.ScreenshotCapture, false);
        Set(MonitoringCapability.AutoScreenshotCapture, false);
        Set(MonitoringCapability.IdentityVerification, false);

        await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        _switcher.Verify(s => s.SwitchToTenantAsync(
            It.Is<TenantRegistryEntry>(e => e.TenantId == _tenantId && e.Slug == "test"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Employee_work_mode_has_desktop_tray_enabled_returns_tray_clock_in_enabled_true()
    {
        _todayState.Setup(t => t.ResolveContextAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.Success(BuildContext(
                new AllowedClockInMethods(Web: true, DesktopTray: true, Biometric: false, PhotoRequired: false, LocationRequired: false, AllowedRadiusMeters: null))));

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TrayClockInEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Today_state_resolution_failure_returns_tray_clock_in_enabled_false()
    {
        _todayState.Setup(t => t.ResolveContextAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayContext>.NotFound("no employee"));

        var result = await CreateSut().Handle(new GetEffectiveTrayPolicyQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TrayClockInEnabled.Should().BeFalse();
    }

    private sealed class FrozenClock : IDateTimeProvider
    {
        public FrozenClock(DateTimeOffset utcNow) => UtcNow = utcNow;
        public DateTimeOffset UtcNow { get; }
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }
}
