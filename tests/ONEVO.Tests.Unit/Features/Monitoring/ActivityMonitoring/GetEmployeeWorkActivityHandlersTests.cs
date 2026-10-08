using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeActivityByHour;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeAppUsage;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeWorkPattern;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class GetEmployeeWorkActivityHandlersTests
{
    private readonly Mock<IEmployeeReadAccessGuard> _guard = new();
    private readonly Mock<IActivitySnapshotRepository> _snapshots = new();
    private readonly Mock<IAppUsageSnapshotRepository> _apps = new();
    private readonly Mock<IMeetingSignalRepository> _meetings = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IMonitoringToggleResolver> _toggles = new();
    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public GetEmployeeWorkActivityHandlersTests()
    {
        _user.SetupGet(u => u.TenantId).Returns(_tenantId);
        _clock.SetupGet(c => c.Today).Returns(new DateOnly(2026, 9, 15));
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Success(new EmployeeListItemResponse(
                _employeeId, "E-001", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null)));
        _toggles.Setup(t => t.IsEnabledForEmployeeAsync(_tenantId, _employeeId, It.IsAny<MonitoringCapability>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _apps.Setup(a => a.GetMinutesByProcessAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AppProcessMinutesRow("code.exe", 72, DateTimeOffset.Parse("2026-09-15T09:05:00+00:00")) });
        _apps.Setup(a => a.GetSamplesForProcessesAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppProcessSampleRow>());
        _snapshots.Setup(s => s.GetWindowsByEmployeeRangeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActivitySnapshot>());
        _meetings.Setup(m => m.GetByEmployeeRangeAsync(_tenantId, _employeeId, It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MeetingSignal>());
    }

    private GetEmployeeActivityByHourQueryHandler ByHour() =>
        new(_guard.Object, _snapshots.Object, _legalEntities.Object, _toggles.Object, _user.Object, _clock.Object);
    private GetEmployeeAppUsageQueryHandler Apps() =>
        new(_guard.Object, _apps.Object, _toggles.Object, _user.Object, _clock.Object);
    private GetEmployeeWorkPatternQueryHandler Pattern() =>
        new(_guard.Object, _snapshots.Object, _meetings.Object, _apps.Object, _toggles.Object, _user.Object, _clock.Object);

    [Fact]
    public async Task ByHour_WhenActivityMonitoringIsOff_ReturnsDisabled_WithoutReadingSnapshots()
    {
        _toggles.Setup(t => t.IsEnabledForEmployeeAsync(_tenantId, _employeeId, MonitoringCapability.ActivityMonitoring, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await ByHour().Handle(new GetEmployeeActivityByHourQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.ActivityMonitoringEnabled.Should().BeFalse();
        result.Value.Hours.Should().BeEmpty();
        _snapshots.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ByHour_Rejects_MoreThan31Days()
    {
        var result = await ByHour().Handle(
            new GetEmployeeActivityByHourQuery(_employeeId, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 30)), CancellationToken.None);

        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ByHour_ReturnsTwentyFourUtcHours_WhenTheEmployeeHasNoLegalEntity()
    {
        _snapshots.Setup(s => s.GetActiveSecondsByHalfHourAsync(_tenantId, _employeeId,
                DateTimeOffset.Parse("2026-09-01T00:00:00+00:00"), DateTimeOffset.Parse("2026-10-01T00:00:00+00:00"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ActivitySlotRow(DateTimeOffset.Parse("2026-09-02T09:00:00+00:00"), 1200) });

        var result = await ByHour().Handle(new GetEmployeeActivityByHourQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.Timezone.Should().Be("UTC");
        result.Value.Hours.Should().HaveCount(24);
        result.Value.Hours[9].ActiveMinutes.Should().Be(20);
    }

    [Fact]
    public async Task Apps_PassesThroughGuardFailure()
    {
        _guard.Setup(g => g.EnsureCanRead(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeListItemResponse>.Forbidden("no"));

        var result = await Apps().Handle(new GetEmployeeAppUsageQuery(_employeeId, null, null), CancellationToken.None);

        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Apps_ReturnsTopAppsWithTotalMinutes()
    {
        var result = await Apps().Handle(new GetEmployeeAppUsageQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.ApplicationTrackingEnabled.Should().BeTrue();
        result.Value.TotalMinutes.Should().Be(72);
        result.Value.Apps.Should().ContainSingle().Which.AppName.Should().Be("code.exe");
    }

    [Fact]
    public async Task Pattern_OmitsTopApp_WhenApplicationTrackingIsOff()
    {
        _toggles.Setup(t => t.IsEnabledForEmployeeAsync(_tenantId, _employeeId, MonitoringCapability.ApplicationTracking, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Pattern().Handle(new GetEmployeeWorkPatternQuery(_employeeId, null, null), CancellationToken.None);

        result.Value!.ActivityMonitoringEnabled.Should().BeTrue();
        result.Value.TopApp.Should().BeNull();
        _apps.VerifyNoOtherCalls();
    }
}
