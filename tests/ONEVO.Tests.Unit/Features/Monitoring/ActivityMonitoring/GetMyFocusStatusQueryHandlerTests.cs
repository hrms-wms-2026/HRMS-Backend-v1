using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyFocusStatus;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class GetMyFocusStatusQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _dateTime = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IActivitySnapshotRepository> _snapshots = new();

    private GetMyFocusStatusQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _dateTime.SetupGet(x => x.UtcNow).Returns(Now);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, UserId = UserId, TenantId = TenantId });

        return new GetMyFocusStatusQueryHandler(
            _currentUser.Object, _dateTime.Object, _employees.Object, _snapshots.Object);
    }

    /// <summary>Builds N contiguous 5-minute active snapshots on the same process, ending at
    /// `endedAt`, so ActivityTimelineBuilder classifies them into one streak.</summary>
    private static List<ActivitySnapshot> BuildContiguousActiveSnapshots(int count, DateTimeOffset endedAt, string process = "code.exe")
    {
        var snapshots = new List<ActivitySnapshot>();
        for (var i = count; i >= 1; i--)
        {
            snapshots.Add(new ActivitySnapshot
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                EmployeeId = EmployeeId,
                AgentDeviceId = Guid.NewGuid(),
                CapturedAt = endedAt.AddMinutes(-5 * (i - 1)),
                ActiveSeconds = 300,
                IdleSeconds = 0,
                ForegroundProcessName = process,
                CreatedAt = endedAt
            });
        }
        return snapshots;
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new GetMyFocusStatusQueryHandler(_currentUser.Object, _dateTime.Object, _employees.Object, _snapshots.Object);

        var result = await sut.Handle(new GetMyFocusStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsNotFound()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);
        var sut = new GetMyFocusStatusQueryHandler(_currentUser.Object, _dateTime.Object, _employees.Object, _snapshots.Object);

        var result = await sut.Handle(new GetMyFocusStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_NoSnapshotsToday_ReturnsNotDue()
    {
        var sut = BuildSut();
        _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, new DateOnly(2026, 8, 27), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await sut.Handle(new GetMyFocusStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsBreakReminderDue.Should().BeFalse();
        result.Value.ContinuousFocusMinutes.Should().Be(0);
    }

    [Fact]
    public async Task Handle_OngoingFocusStreakAtNinetyMinutes_IsBreakReminderDue()
    {
        var sut = BuildSut();
        // 19 snapshots x 5 min, ending exactly at "now" -> 95-minute contiguous focus streak.
        var snapshots = BuildContiguousActiveSnapshots(19, Now);
        _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, new DateOnly(2026, 8, 27), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var result = await sut.Handle(new GetMyFocusStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContinuousFocusMinutes.Should().Be(95);
        result.Value.IsBreakReminderDue.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_OngoingFocusStreakUnderThreshold_IsNotDueButReportsMinutes()
    {
        var sut = BuildSut();
        // 8 snapshots x 5 min = 40-minute streak: enough to classify as "focus" (>=30) but under the 90-min reminder threshold.
        var snapshots = BuildContiguousActiveSnapshots(8, Now);
        _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, new DateOnly(2026, 8, 27), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var result = await sut.Handle(new GetMyFocusStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContinuousFocusMinutes.Should().Be(40);
        result.Value.IsBreakReminderDue.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LongFocusStreakThatEndedAWhileAgo_IsNotTreatedAsOngoing()
    {
        var sut = BuildSut();
        // Same 95-minute streak, but the last snapshot is 20 minutes stale relative to "now" -
        // e.g. the employee clocked out a while ago. Must not report this as a live streak.
        var snapshots = BuildContiguousActiveSnapshots(19, Now.AddMinutes(-20));
        _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, new DateOnly(2026, 8, 27), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var result = await sut.Handle(new GetMyFocusStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContinuousFocusMinutes.Should().Be(0);
        result.Value.IsBreakReminderDue.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LastSegmentIsIdle_ReportsZeroFocusMinutes()
    {
        var sut = BuildSut();
        var snapshots = new List<ActivitySnapshot>
        {
            new()
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = Now, ActiveSeconds = 0, IdleSeconds = 300,
                ForegroundProcessName = "code.exe", CreatedAt = Now
            }
        };
        _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(TenantId, EmployeeId, new DateOnly(2026, 8, 27), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var result = await sut.Handle(new GetMyFocusStatusQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContinuousFocusMinutes.Should().Be(0);
        result.Value.IsBreakReminderDue.Should().BeFalse();
    }
}
