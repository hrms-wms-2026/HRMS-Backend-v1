using FluentAssertions;
using Moq;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

namespace ONEVO.Tests.Unit.Services.Monitoring.ActivityMonitoring;

public class ActivityLiveDaySummaryTests
{
    [Fact]
    public async Task ComposeAsync_ReturnsNullWhenDayHasNoSnapshots()
    {
        var snapshots = new Mock<IActivitySnapshotRepository>();
        snapshots.Setup(x => x.GetAllByEmployeeDateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActivitySnapshot>());
        var apps = new Mock<IAppUsageSnapshotRepository>();
        apps.Setup(x => x.GetAllByEmployeeDateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppUsageSnapshot>());
        var meetings = new Mock<IMeetingSignalRepository>();
        meetings.Setup(x => x.GetAllByEmployeeDateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MeetingSignal>());
        var sut = new ActivityLiveDaySummary(snapshots.Object, apps.Object, meetings.Object);

        var result = await sut.ComposeAsync(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 22), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ComposeAsync_ReturnsTopAppsWhenOnlyAppUsageExists()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var capturedAt = new DateTimeOffset(2026, 9, 23, 3, 20, 0, TimeSpan.Zero);
        var snapshots = new Mock<IActivitySnapshotRepository>();
        snapshots.Setup(x => x.GetAllByEmployeeDateAsync(tenantId, employeeId, new DateOnly(2026, 9, 23), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ActivitySnapshot>());
        var apps = new Mock<IAppUsageSnapshotRepository>();
        apps.Setup(x => x.GetAllByEmployeeDateAsync(tenantId, employeeId, new DateOnly(2026, 9, 23), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AppUsageSnapshot>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employeeId,
                    AgentDeviceId = Guid.NewGuid(),
                    CapturedAt = capturedAt,
                    ProcessName = "chrome.exe",
                    CreatedAt = capturedAt
                }
            });
        var meetings = new Mock<IMeetingSignalRepository>();
        meetings.Setup(x => x.GetAllByEmployeeDateAsync(tenantId, employeeId, new DateOnly(2026, 9, 23), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MeetingSignal>());
        var sut = new ActivityLiveDaySummary(snapshots.Object, apps.Object, meetings.Object);

        var result = await sut.ComposeAsync(tenantId, employeeId, new DateOnly(2026, 9, 23), CancellationToken.None);

        result.Should().NotBeNull();
        result!.TopApps.Should().ContainSingle(app => app.AppName == "chrome.exe" && app.TotalSeconds == 60);
    }

    [Fact]
    public async Task ComposeAsync_SumsSnapshotMinutes()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var capturedAt = new DateTimeOffset(2026, 9, 22, 4, 0, 0, TimeSpan.Zero);
        var snapshots = new Mock<IActivitySnapshotRepository>();
        snapshots.Setup(x => x.GetAllByEmployeeDateAsync(tenantId, employeeId, new DateOnly(2026, 9, 22), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActivitySnapshot>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employeeId,
                    AgentDeviceId = Guid.NewGuid(),
                    CapturedAt = capturedAt,
                    ActiveSeconds = 120,
                    IdleSeconds = 60,
                    IntensityScore = 50,
                    CreatedAt = capturedAt
                }
            });
        var apps = new Mock<IAppUsageSnapshotRepository>();
        apps.Setup(x => x.GetAllByEmployeeDateAsync(tenantId, employeeId, new DateOnly(2026, 9, 22), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppUsageSnapshot>());
        var meetings = new Mock<IMeetingSignalRepository>();
        meetings.Setup(x => x.GetAllByEmployeeDateAsync(tenantId, employeeId, new DateOnly(2026, 9, 22), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MeetingSignal>());
        var sut = new ActivityLiveDaySummary(snapshots.Object, apps.Object, meetings.Object);

        var result = await sut.ComposeAsync(tenantId, employeeId, new DateOnly(2026, 9, 22), CancellationToken.None);

        result.Should().NotBeNull();
        result!.TotalActiveMinutes.Should().Be(2);
        result.TotalIdleMinutes.Should().Be(1);
    }
}
