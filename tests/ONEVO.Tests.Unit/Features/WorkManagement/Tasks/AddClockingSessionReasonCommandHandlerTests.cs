using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddClockingSessionReason;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class AddClockingSessionReasonCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();

    private (AddClockingSessionReasonCommandHandler Handler, Mock<ITaskClockingSessionRepository> Sessions, Guid CallerEmployeeId, TaskClockingSession Session)
        ArrangeReasonHandler(bool sessionOwnedByCaller)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerEmployeeId);

        var session = new TaskClockingSession
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TaskId = Guid.NewGuid(),
            EmployeeId = sessionOwnedByCaller ? CallerEmployeeId : OtherEmployeeId,
            ClockInAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        var sessions = new Mock<ITaskClockingSessionRepository>();
        sessions.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, session.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> operation, CancellationToken ct) => operation(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AddClockingSessionReasonCommandHandler(
            currentUser.Object, identity.Object, sessions.Object, unitOfWork.Object);
        return (handler, sessions, CallerEmployeeId, session);
    }

    [Fact]
    public async Task Handle_CallerOwnsTheSession_SetsReason()
    {
        var (handler, sessions, _, session) = ArrangeReasonHandler(true);

        var result = await handler.Handle(
            new AddClockingSessionReasonCommand(session.Id, "context on why this took long"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("context on why this took long", session.Reason);
        sessions.Verify(x => x.Update(session), Times.Once);
    }

    [Fact]
    public async Task Handle_CallerDoesNotOwnTheSession_ReturnsForbidden()
    {
        var (handler, sessions, _, session) = ArrangeReasonHandler(false);

        var result = await handler.Handle(
            new AddClockingSessionReasonCommand(session.Id, "not mine"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(session.Reason);
        sessions.Verify(x => x.Update(It.IsAny<TaskClockingSession>()), Times.Never);
    }
}
