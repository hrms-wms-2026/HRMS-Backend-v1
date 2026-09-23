using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.EditTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class EditTaskCommentCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AuthorEmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid CommentId = Guid.NewGuid();

    private (EditTaskCommentCommandHandler Handler, Mock<ITaskCommentRepository> Comments, Mock<ITaskCommentLogRepository> Logs)
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
        var linker = new Mock<ITaskAssetLinker>();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [AuthorEmployeeId] = "Priya" });
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new EditTaskCommentCommandHandler(
            currentUser.Object, access.Object, comments.Object, logs.Object, linker.Object, identity.Object, unitOfWork.Object);
        return (handler, comments, logs);
    }

    private static TaskComment Existing() => new()
    {
        Id = CommentId, TenantId = TenantId, TaskId = TaskId, EmployeeId = AuthorEmployeeId,
        Content = "original", IsEdited = false, CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_Author_UpdatesContentAndWritesLog()
    {
        var (handler, comments, logs) = Build(AuthorEmployeeId, Existing());

        var result = await handler.Handle(new EditTaskCommentCommand(CommentId, "edited text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("edited text", result.Value!.Content);
        Assert.True(result.Value.IsEdited);
        logs.Verify(x => x.AddAsync(It.Is<TaskCommentLog>(l =>
            l.CommentId == CommentId && l.EmployeeId == AuthorEmployeeId && l.Action == TaskCommentLogActions.Edited), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAuthor_ReturnsForbidden()
    {
        var otherEmployeeId = Guid.NewGuid();
        var (handler, _, logs) = Build(otherEmployeeId, Existing());

        var result = await handler.Handle(new EditTaskCommentCommand(CommentId, "edited text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        logs.Verify(x => x.AddAsync(It.IsAny<TaskCommentLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CommentNotFound_ReturnsNotFound()
    {
        var (handler, _, _) = Build(AuthorEmployeeId, null);

        var result = await handler.Handle(new EditTaskCommentCommand(CommentId, "text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyDeletedComment_ReturnsNotFound()
    {
        var deleted = Existing();
        deleted.IsDeleted = true;
        var (handler, _, _) = Build(AuthorEmployeeId, deleted);

        var result = await handler.Handle(new EditTaskCommentCommand(CommentId, "text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
