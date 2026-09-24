using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.RemoveTaskCommentReaction;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskCommentReactionCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid CommentId = Guid.NewGuid();

    private static TaskComment Comment() => new() { Id = CommentId, TenantId = TenantId, TaskId = TaskId, EmployeeId = Guid.NewGuid() };

    private (Mock<ICurrentUser> CurrentUser, Mock<ITaskAccessResolver> Access, Mock<ITaskCommentRepository> Comments, Mock<ITaskCommentReactionRepository> Reactions) BuildDeps()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var access = new Mock<ITaskAccessResolver>();
        access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.Success(new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, EmployeeId)));

        var comments = new Mock<ITaskCommentRepository>();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, CommentId, It.IsAny<CancellationToken>())).ReturnsAsync(Comment());

        var reactions = new Mock<ITaskCommentReactionRepository>();
        return (currentUser, access, comments, reactions);
    }

    [Fact]
    public async Task AddReaction_NewEmoji_AddsIt()
    {
        var (currentUser, access, comments, reactions) = BuildDeps();
        reactions.Setup(x => x.GetForEmployeeAsync(TenantId, CommentId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync((TaskCommentReaction?)null);
        var handler = new AddTaskCommentReactionCommandHandler(currentUser.Object, access.Object, comments.Object, reactions.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new AddTaskCommentReactionCommand(CommentId, "👍"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        reactions.Verify(x => x.AddAsync(It.Is<TaskCommentReaction>(r => r.CommentId == CommentId && r.EmployeeId == EmployeeId && r.Emoji == "👍"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddReaction_SameEmojiAlreadyExists_IsIdempotentNoOp()
    {
        var (currentUser, access, comments, reactions) = BuildDeps();
        var existing = new TaskCommentReaction { CommentId = CommentId, EmployeeId = EmployeeId, Emoji = "👍" };
        reactions.Setup(x => x.GetForEmployeeAsync(TenantId, CommentId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new AddTaskCommentReactionCommandHandler(currentUser.Object, access.Object, comments.Object, reactions.Object, unitOfWork.Object);

        var result = await handler.Handle(new AddTaskCommentReactionCommand(CommentId, "👍"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("👍", existing.Emoji);
        reactions.Verify(x => x.AddAsync(It.IsAny<TaskCommentReaction>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddReaction_DifferentEmojiAlreadyExists_ReplacesItInPlace()
    {
        var (currentUser, access, comments, reactions) = BuildDeps();
        var existing = new TaskCommentReaction { CommentId = CommentId, EmployeeId = EmployeeId, Emoji = "👍" };
        reactions.Setup(x => x.GetForEmployeeAsync(TenantId, CommentId, EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var unitOfWork = new Mock<IUnitOfWork>();
        var handler = new AddTaskCommentReactionCommandHandler(currentUser.Object, access.Object, comments.Object, reactions.Object, unitOfWork.Object);

        var result = await handler.Handle(new AddTaskCommentReactionCommand(CommentId, "🎉"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("🎉", existing.Emoji);
        Assert.NotNull(existing.UpdatedAt);
        reactions.Verify(x => x.AddAsync(It.IsAny<TaskCommentReaction>(), It.IsAny<CancellationToken>()), Times.Never);
        reactions.Verify(x => x.RemoveAsync(It.IsAny<TaskCommentReaction>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveReaction_Existing_RemovesIt()
    {
        var (currentUser, access, comments, reactions) = BuildDeps();
        var existing = new TaskCommentReaction { CommentId = CommentId, EmployeeId = EmployeeId, Emoji = "👍" };
        reactions.Setup(x => x.GetAsync(TenantId, CommentId, EmployeeId, "👍", It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        var handler = new RemoveTaskCommentReactionCommandHandler(currentUser.Object, access.Object, comments.Object, reactions.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new RemoveTaskCommentReactionCommand(CommentId, "👍"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        reactions.Verify(x => x.RemoveAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveReaction_DoesNotExist_IsIdempotentNoOp()
    {
        var (currentUser, access, comments, reactions) = BuildDeps();
        reactions.Setup(x => x.GetAsync(TenantId, CommentId, EmployeeId, "👍", It.IsAny<CancellationToken>())).ReturnsAsync((TaskCommentReaction?)null);
        var handler = new RemoveTaskCommentReactionCommandHandler(currentUser.Object, access.Object, comments.Object, reactions.Object, Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new RemoveTaskCommentReactionCommand(CommentId, "👍"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        reactions.Verify(x => x.RemoveAsync(It.IsAny<TaskCommentReaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
