using Moq;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskFile;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class GetTaskFileQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private readonly Mock<IEntityAssetRepository> _assets = new();
    private readonly Mock<ITaskCommentRepository> _comments = new();
    private readonly Mock<ITaskAccessResolver> _access = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static FileRecordDto FileRecord(Guid uploadedBy) => new(
        FileId, TenantId, "k", "a.png", "a.png", "image/png", 10, new string('a', 64),
        "available", DateTimeOffset.UtcNow, uploadedBy, null);

    private GetTaskFileQueryHandler Build()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);

        return new GetTaskFileQueryHandler(
            _currentUser.Object, _assets.Object, _comments.Object, _access.Object, _fileStorage.Object);
    }

    [Fact]
    public async Task Handle_UnlinkedFileOwnedByCaller_StreamsIt()
    {
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        _fileStorage.Setup(x => x.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(FileRecord(UserId)));
        _fileStorage.Setup(x => x.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(Stream.Null, "image/png")));

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_UnlinkedFileOwnedBySomeoneElse_ReturnsNotFound()
    {
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        _fileStorage.Setup(x => x.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(FileRecord(Guid.NewGuid())));

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_LinkedToAccessibleTask_StreamsIt()
    {
        var link = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Task, OwnerId = TaskId, AssetPurpose = "task_attachment", FileRecordId = FileId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(link);
        _access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.Success(new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, EmployeeId)));
        _fileStorage.Setup(x => x.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(Stream.Null, "image/png")));

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_LinkedToInaccessibleTask_ReturnsNotFound()
    {
        var link = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Task, OwnerId = TaskId, AssetPurpose = "task_attachment", FileRecordId = FileId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(link);
        _access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.NotFound("Task not found."));

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_FileLinkedToCommentOnViewableTask_ReturnsStream()
    {
        var commentId = Guid.NewGuid();
        var link = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Comment, OwnerId = commentId, AssetPurpose = "comment_attachment", FileRecordId = FileId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(link);
        _comments.Setup(x => x.GetByIdForTenantAsync(TenantId, commentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskComment { Id = commentId, TaskId = TaskId, TenantId = TenantId });
        _access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.Success(new TaskAccessContext(new WorkTask { Id = TaskId, TenantId = TenantId }, Guid.NewGuid())));
        _fileStorage.Setup(x => x.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(Stream.Null, "image/png")));

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_FileLinkedToCommentOnNonViewableTask_ReturnsNotFound()
    {
        var commentId = Guid.NewGuid();
        var link = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Comment, OwnerId = commentId, AssetPurpose = "comment_attachment", FileRecordId = FileId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(link);
        _comments.Setup(x => x.GetByIdForTenantAsync(TenantId, commentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskComment { Id = commentId, TaskId = TaskId, TenantId = TenantId });
        _access.Setup(x => x.ResolveViewableTaskAsync(TenantId, UserId, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TaskAccessContext>.NotFound("Task not found."));

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
