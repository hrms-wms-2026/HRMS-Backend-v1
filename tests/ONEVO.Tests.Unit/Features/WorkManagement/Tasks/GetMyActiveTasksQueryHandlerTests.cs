using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyActiveTasks;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class GetMyActiveTasksQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _dateTime = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private GetMyActiveTasksQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        return new GetMyActiveTasksQueryHandler(_currentUser.Object, _dateTime.Object, _identity.Object, _tasks.Object);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new GetMyActiveTasksQueryHandler(_currentUser.Object, _dateTime.Object, _identity.Object, _tasks.Object);

        var result = await sut.Handle(new GetMyActiveTasksQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        var sut = new GetMyActiveTasksQueryHandler(_currentUser.Object, _dateTime.Object, _identity.Object, _tasks.Object);

        var result = await sut.Handle(new GetMyActiveTasksQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_PassesSevenDayCutoffByDefaultAndMapsIsOverdue()
    {
        var sut = BuildSut();
        var expectedCutoff = new DateOnly(2026, 9, 3); // 2026-08-27 + 7 days
        _tasks.Setup(x => x.GetMyActiveTasksAsync(TenantId, EmployeeId, expectedCutoff, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MyTaskRow(Guid.NewGuid(), "T-1", "Overdue task", new DateOnly(2026, 8, 20), Guid.NewGuid(), "Project A", Guid.NewGuid(), "high"),
                new MyTaskRow(Guid.NewGuid(), "T-2", "Upcoming task", new DateOnly(2026, 8, 30), Guid.NewGuid(), "Project A", Guid.NewGuid(), "medium")
            ]);

        var result = await sut.Handle(new GetMyActiveTasksQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Tasks.Count);
        Assert.True(result.Value.Tasks[0].IsOverdue);
        Assert.False(result.Value.Tasks[1].IsOverdue);
    }

    [Fact]
    public async Task Handle_CustomUpcomingDays_PassesCorrectCutoff()
    {
        var sut = BuildSut();
        var expectedCutoff = new DateOnly(2026, 8, 30); // 2026-08-27 + 3 days
        _tasks.Setup(x => x.GetMyActiveTasksAsync(TenantId, EmployeeId, expectedCutoff, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await sut.Handle(new GetMyActiveTasksQuery(UpcomingDays: 3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _tasks.VerifyAll();
    }
}
