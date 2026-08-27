using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyTaskProgress;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public sealed class GetMyTaskProgressQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 8, 27);

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IDateTimeProvider> _dateTime = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IWorkTaskRepository> _tasks = new();

    private GetMyTaskProgressQueryHandler BuildSut()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _dateTime.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(Today, TimeOnly.MinValue, TimeSpan.Zero));
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmployeeId);

        return new GetMyTaskProgressQueryHandler(_currentUser.Object, _dateTime.Object, _identity.Object, _tasks.Object);
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsForbidden()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var sut = new GetMyTaskProgressQueryHandler(_currentUser.Object, _dateTime.Object, _identity.Object, _tasks.Object);

        var result = await sut.Handle(new GetMyTaskProgressQuery(), CancellationToken.None);

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
        var sut = new GetMyTaskProgressQueryHandler(_currentUser.Object, _dateTime.Object, _identity.Object, _tasks.Object);

        var result = await sut.Handle(new GetMyTaskProgressQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_BucketsRowsIntoCompletedOverdueInProgressAndNotStarted()
    {
        var sut = BuildSut();
        _tasks.Setup(x => x.GetMyTaskProgressRowsAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TaskProgressRow(MarksTaskComplete: true, DueDate: new DateOnly(2026, 8, 1), ProgressPercent: 100), // completed
                new TaskProgressRow(MarksTaskComplete: false, DueDate: new DateOnly(2026, 8, 20), ProgressPercent: 40), // overdue (due < today), even though progress > 0
                new TaskProgressRow(MarksTaskComplete: false, DueDate: new DateOnly(2026, 9, 5), ProgressPercent: 30), // in progress
                new TaskProgressRow(MarksTaskComplete: false, DueDate: null, ProgressPercent: 0), // not started, no due date
                new TaskProgressRow(MarksTaskComplete: false, DueDate: new DateOnly(2026, 9, 10), ProgressPercent: 0) // not started, future due date
            ]);

        var result = await sut.Handle(new GetMyTaskProgressQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Completed);
        Assert.Equal(1, result.Value.Overdue);
        Assert.Equal(1, result.Value.InProgress);
        Assert.Equal(2, result.Value.NotStarted);
        Assert.Equal(5, result.Value.Total);
    }

    [Fact]
    public async Task Handle_TaskDueExactlyToday_IsNotOverdue()
    {
        var sut = BuildSut();
        _tasks.Setup(x => x.GetMyTaskProgressRowsAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TaskProgressRow(MarksTaskComplete: false, DueDate: Today, ProgressPercent: 0)]);

        var result = await sut.Handle(new GetMyTaskProgressQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Overdue);
        Assert.Equal(1, result.Value.NotStarted);
    }

    [Fact]
    public async Task Handle_NoTasks_ReturnsAllZeros()
    {
        var sut = BuildSut();
        _tasks.Setup(x => x.GetMyTaskProgressRowsAsync(TenantId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await sut.Handle(new GetMyTaskProgressQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Total);
    }
}
