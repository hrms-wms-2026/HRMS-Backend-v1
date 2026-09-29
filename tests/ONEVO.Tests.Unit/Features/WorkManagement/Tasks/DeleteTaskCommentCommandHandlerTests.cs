using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DeleteTaskCommentCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AuthorEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid CommentId = Guid.NewGuid();

    private (DeleteTaskCommentCommandHandler Handler, Mock<ITaskCommentLogRepository> Logs, Mock<IUnitOfWork> UnitOfWork)
        Build(Guid callerEmployeeId, TaskComment? existingComment)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var access = new Mock<ITaskAccessResolver>();
        access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.Success(
                new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, callerEmployeeId)));

        var comments = new Mock<ITaskCommentRepository>();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, CommentId, It.IsAny<CancellationToken>())).ReturnsAsync(existingComment);

        var logs = new Mock<ITaskCommentLogRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new DeleteTaskCommentCommandHandler(currentUser.Object, access.Object, comments.Object, logs.Object, unitOfWork.Object);
        return (handler, logs, unitOfWork);
    }

    private static TaskComment Existing() => new()
    {
        Id = CommentId, TenantId = TenantId, TaskId = TaskId, EmployeeId = AuthorEmployeeId,
        Content = "to delete", CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_Author_SoftDeletesAndWritesLog()
    {
        var comment = Existing();
        var (handler, logs, unitOfWork) = Build(AuthorEmployeeId, comment);

        var result = await handler.Handle(new DeleteTaskCommentCommand(CommentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(comment.IsDeleted);
        Assert.NotNull(comment.DeletedAt);
        logs.Verify(x => x.AddAsync(It.Is<TaskCommentLog>(l =>
            l.CommentId == CommentId && l.EmployeeId == AuthorEmployeeId && l.Action == TaskCommentLogActions.Deleted), It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAuthor_ReturnsForbidden()
    {
        var comment = Existing();
        var otherEmployeeId = Guid.NewGuid();
        var (handler, logs, _) = Build(otherEmployeeId, comment);

        var result = await handler.Handle(new DeleteTaskCommentCommand(CommentId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.False(comment.IsDeleted);
        logs.Verify(x => x.AddAsync(It.IsAny<TaskCommentLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CommentNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = Build(AuthorEmployeeId, null);

        var result = await handler.Handle(new DeleteTaskCommentCommand(CommentId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
