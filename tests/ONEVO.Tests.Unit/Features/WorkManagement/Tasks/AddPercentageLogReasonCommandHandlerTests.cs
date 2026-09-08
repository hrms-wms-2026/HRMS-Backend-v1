using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddPercentageLogReason;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class AddPercentageLogReasonCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerEmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();

    private (AddPercentageLogReasonCommandHandler Handler, Mock<ITaskPercentageLogRepository> Logs, Guid CallerEmployeeId, TaskPercentageLog Log)
        ArrangePercentageLogReasonHandler(bool logOwnedByCaller, bool authenticated = true,
            bool employeeExists = true, bool logExists = true)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(authenticated);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(employeeExists ? CallerEmployeeId : null);

        var log = new TaskPercentageLog
        {
            Id = Guid.NewGuid(), TenantId = TenantId, TaskId = Guid.NewGuid(),
            EmployeeId = logOwnedByCaller ? CallerEmployeeId : OtherEmployeeId,
            PreviousPercent = 20, NewPercent = 40, Source = TaskPercentageLogSources.Push,
            ChangedAt = DateTimeOffset.UtcNow
        };
        var logs = new Mock<ITaskPercentageLogRepository>();
        logs.Setup(x => x.GetTrackedByIdForTenantAsync(TenantId, log.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(logExists ? log : null);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result>> operation, CancellationToken ct) => operation(ct));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new AddPercentageLogReasonCommandHandler(
            currentUser.Object, identity.Object, logs.Object, unitOfWork.Object);
        return (handler, logs, CallerEmployeeId, log);
    }

    [Fact]
    public async Task Handle_CallerOwnsTheLog_SetsReason()
    {
        var (handler, logs, _, log) = ArrangePercentageLogReasonHandler(true);

        var result = await handler.Handle(
            new AddPercentageLogReasonCommand(log.Id, "why the estimate changed"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("why the estimate changed", log.Reason);
        logs.Verify(x => x.Update(log), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsForbidden()
    {
        var (handler, logs, _, log) = ArrangePercentageLogReasonHandler(true, authenticated: false);

        var result = await handler.Handle(new AddPercentageLogReasonCommand(log.Id, "note"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(log.Reason);
        logs.Verify(x => x.Update(It.IsAny<TaskPercentageLog>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoEmployeeRecord_ReturnsForbidden()
    {
        var (handler, logs, _, log) = ArrangePercentageLogReasonHandler(true, employeeExists: false);

        var result = await handler.Handle(new AddPercentageLogReasonCommand(log.Id, "note"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(log.Reason);
        logs.Verify(x => x.Update(It.IsAny<TaskPercentageLog>()), Times.Never);
    }

    [Fact]
    public async Task Handle_LogNotFound_ReturnsNotFound()
    {
        var (handler, logs, _, log) = ArrangePercentageLogReasonHandler(true, logExists: false);

        var result = await handler.Handle(new AddPercentageLogReasonCommand(log.Id, "note"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        Assert.Null(log.Reason);
        logs.Verify(x => x.Update(It.IsAny<TaskPercentageLog>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingReason_IsOverwrittenWithTrimmedValue()
    {
        var (handler, _, _, log) = ArrangePercentageLogReasonHandler(true);
        log.Reason = "old note";

        var result = await handler.Handle(new AddPercentageLogReasonCommand(log.Id, "  new note  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new note", log.Reason);
    }

    [Fact]
    public async Task Handle_CallerDoesNotOwnTheLog_ReturnsForbidden()
    {
        var (handler, logs, _, log) = ArrangePercentageLogReasonHandler(false);

        var result = await handler.Handle(
            new AddPercentageLogReasonCommand(log.Id, "not mine"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(log.Reason);
        logs.Verify(x => x.Update(It.IsAny<TaskPercentageLog>()), Times.Never);
    }
}
