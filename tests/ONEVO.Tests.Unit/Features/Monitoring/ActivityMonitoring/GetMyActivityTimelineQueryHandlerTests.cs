using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.GetMyActivityTimeline;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Monitoring.ActivityMonitoring.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

public sealed class GetMyActivityTimelineQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _dateTime = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IActivitySnapshotRepository> _snapshots = new();

    private GetMyActivityTimelineQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
        _employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = EmployeeId, UserId = UserId, TenantId = TenantId });

        return new GetMyActivityTimelineQueryHandler(
            _currentUser.Object, _dateTime.Object, _employees.Object, _snapshots.Object);
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new GetMyActivityTimelineQueryHandler(
            _currentUser.Object, _dateTime.Object, _employees.Object, _snapshots.Object);

        var result = await sut.Handle(new GetMyActivityTimelineQuery(null), CancellationToken.None);

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
        var sut = new GetMyActivityTimelineQueryHandler(
            _currentUser.Object, _dateTime.Object, _employees.Object, _snapshots.Object);

        var result = await sut.Handle(new GetMyActivityTimelineQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_NoDateProvided_DefaultsToUtcToday()
    {
        var sut = BuildSut();
        _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(
                TenantId, EmployeeId, new DateOnly(2026, 8, 27), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await sut.Handle(new GetMyActivityTimelineQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Date.Should().Be(new DateOnly(2026, 8, 27));
        result.Value.Segments.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsClassifiedSegmentsForRequestedDate()
    {
        var sut = BuildSut();
        var requestedDate = new DateOnly(2026, 8, 20);
        var baseTime = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);
        _snapshots.Setup(x => x.GetAllByEmployeeDateAsync(
                TenantId, EmployeeId, requestedDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ActivitySnapshot
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                    CapturedAt = baseTime.AddMinutes(5), ActiveSeconds = 300, IdleSeconds = 0,
                    ForegroundProcessName = "code.exe", CreatedAt = baseTime
                }
            ]);

        var result = await sut.Handle(new GetMyActivityTimelineQuery(requestedDate), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Date.Should().Be(requestedDate);
        result.Value.Segments.Should().ContainSingle();
        result.Value.Segments[0].Type.Should().Be("idle"); // 5 min active, under the 30-min focus threshold
    }
}
