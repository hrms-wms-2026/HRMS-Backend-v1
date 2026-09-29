using Moq;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetCommentsForTask;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetCommentsForTaskQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private (GetCommentsForTaskQueryHandler Handler, Mock<ITaskCommentRepository> Comments) Build(IReadOnlyList<TaskComment> comments)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var access = new Mock<ITaskAccessResolver>();
        access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.Success(new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, EmployeeId)));

        var commentRepo = new Mock<ITaskCommentRepository>();
        commentRepo.Setup(x => x.GetForTaskAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(comments);

        var reactions = new Mock<ITaskCommentReactionRepository>();
        reactions.Setup(x => x.GetForCommentIdsAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TaskCommentReaction>());

        var assets = new Mock<IEntityAssetRepository>();
        assets.Setup(x => x.ListByOwnersAsync(TenantId, EntityAssetOwnerTypes.Comment, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFileAndOwner>());

        var identity = new Mock<ICallerIdentityResolver>();
        identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(comments.Select(c => c.EmployeeId).Distinct().ToDictionary(id => id, _ => "Priya"));

        var handler = new GetCommentsForTaskQueryHandler(currentUser.Object, access.Object, commentRepo.Object, reactions.Object, assets.Object, identity.Object);
        return (handler, commentRepo);
    }

    [Fact]
    public async Task Handle_TopLevelWithReply_NestsReplyUnderIt()
    {
        var topLevelId = Guid.NewGuid();
        var comments = new List<TaskComment>
        {
            new() { Id = topLevelId, TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "root", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, ParentCommentId = topLevelId, Content = "reply", CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _) = Build(comments);

        var result = await handler.Handle(new GetCommentsForTaskQuery(TaskId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Single(result.Value![0].Replies);
        Assert.Equal("reply", result.Value[0].Replies[0].Content);
    }

    [Fact]
    public async Task Handle_DeletedTopLevelWithNoReplies_IsExcluded()
    {
        var comments = new List<TaskComment>
        {
            new() { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "gone", IsDeleted = true, CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _) = Build(comments);

        var result = await handler.Handle(new GetCommentsForTaskQuery(TaskId), CancellationToken.None);

        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task Handle_DeletedTopLevelWithSurvivingReply_IsTombstoned()
    {
        var topLevelId = Guid.NewGuid();
        var comments = new List<TaskComment>
        {
            new() { Id = topLevelId, TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "gone", IsDeleted = true, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, ParentCommentId = topLevelId, Content = "still here", CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _) = Build(comments);

        var result = await handler.Handle(new GetCommentsForTaskQuery(TaskId), CancellationToken.None);

        Assert.Single(result.Value!);
        Assert.True(result.Value![0].IsDeleted);
        Assert.Equal(string.Empty, result.Value[0].Content);
        Assert.Single(result.Value[0].Replies);
    }

    [Fact]
    public async Task Handle_DeletedReply_IsDroppedFromItsParent()
    {
        var topLevelId = Guid.NewGuid();
        var comments = new List<TaskComment>
        {
            new() { Id = topLevelId, TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "root", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
            new() { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, ParentCommentId = topLevelId, Content = "deleted reply", IsDeleted = true, CreatedAt = DateTimeOffset.UtcNow }
        };
        var (handler, _) = Build(comments);

        var result = await handler.Handle(new GetCommentsForTaskQuery(TaskId), CancellationToken.None);

        Assert.Single(result.Value!);
        Assert.Empty(result.Value![0].Replies);
    }

    [Fact]
    public async Task Handle_MultipleTopLevel_OrderedNewestFirst()
    {
        var older = new TaskComment { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "older", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var newer = new TaskComment { Id = Guid.NewGuid(), TenantId = TenantId, TaskId = TaskId, EmployeeId = EmployeeId, Content = "newer", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, _) = Build(new List<TaskComment> { older, newer });

        var result = await handler.Handle(new GetCommentsForTaskQuery(TaskId), CancellationToken.None);

        Assert.Equal("newer", result.Value![0].Content);
        Assert.Equal("older", result.Value[1].Content);
    }
}
