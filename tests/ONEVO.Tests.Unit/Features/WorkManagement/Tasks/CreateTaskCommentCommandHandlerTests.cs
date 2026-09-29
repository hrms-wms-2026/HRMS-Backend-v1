using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.CreateTaskComment;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class CreateTaskCommentCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private (CreateTaskCommentCommandHandler Handler, Mock<ITaskCommentRepository> Comments, Mock<ITaskAssetLinker> Linker)
        Build(Result<TaskAccessContext>? accessResult = null)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var access = new Mock<ITaskAccessResolver>();
        access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(accessResult ?? Result<TaskAccessContext>.Success(
                new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, EmployeeId)));

        var comments = new Mock<ITaskCommentRepository>();
        var linker = new Mock<ITaskAssetLinker>();
        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [EmployeeId] = "Priya" });
        var unitOfWork = new Mock<IUnitOfWork>();

        var handler = new CreateTaskCommentCommandHandler(
            currentUser.Object, access.Object, comments.Object, linker.Object, identity.Object, unitOfWork.Object);
        return (handler, comments, linker);
    }

    [Fact]
    public async Task Handle_TopLevelComment_CreatesAndSyncsAssets()
    {
        var (handler, comments, linker) = Build();

        var result = await handler.Handle(
            new CreateTaskCommentCommand(TaskId, null, "Looks good", new[] { Guid.NewGuid() }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeId, result.Value!.EmployeeId);
        Assert.Null(result.Value.ParentCommentId);
        comments.Verify(x => x.AddAsync(It.Is<TaskComment>(c => c.TaskId == TaskId && c.EmployeeId == EmployeeId && c.Content == "Looks good"), It.IsAny<CancellationToken>()), Times.Once);
        linker.Verify(x => x.SyncCommentAttachmentsAsync(TenantId, UserId, It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        linker.Verify(x => x.SyncCommentDescriptionImagesAsync(TenantId, UserId, It.IsAny<Guid>(), "Looks good", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReplyToTopLevelComment_ResolvesTaskIdFromParent()
    {
        var (handler, comments, _) = Build();
        var parentId = Guid.NewGuid();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, parentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskComment { Id = parentId, TenantId = TenantId, TaskId = TaskId, ParentCommentId = null });

        var result = await handler.Handle(
            new CreateTaskCommentCommand(null, parentId, "Agreed", Array.Empty<Guid>()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(parentId, result.Value!.ParentCommentId);
        comments.Verify(x => x.AddAsync(It.Is<TaskComment>(c => c.TaskId == TaskId && c.ParentCommentId == parentId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReplyToAReply_ReturnsBadRequest()
    {
        var (handler, comments, _) = Build();
        var topLevelId = Guid.NewGuid();
        var replyId = Guid.NewGuid();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, replyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskComment { Id = replyId, TenantId = TenantId, TaskId = TaskId, ParentCommentId = topLevelId });

        var result = await handler.Handle(
            new CreateTaskCommentCommand(null, replyId, "Can't nest", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ParentCommentNotFound_ReturnsNotFound()
    {
        var (handler, comments, _) = Build();
        var parentId = Guid.NewGuid();
        comments.Setup(x => x.GetByIdForTenantAsync(TenantId, parentId, It.IsAny<CancellationToken>())).ReturnsAsync((TaskComment?)null);

        var result = await handler.Handle(
            new CreateTaskCommentCommand(null, parentId, "text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoTaskVisibility_PropagatesAccessFailure()
    {
        var (handler, _, _) = Build(Result<TaskAccessContext>.NotFound("Task not found."));

        var result = await handler.Handle(
            new CreateTaskCommentCommand(TaskId, null, "text", Array.Empty<Guid>()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
