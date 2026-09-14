using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Commands.DeleteTaskPendingUpload;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class DeleteTaskPendingUploadCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();

    private (DeleteTaskPendingUploadCommandHandler Handler, Mock<IFileStorageService> FileStorage) Build(
        FileRecord? record, EntityAsset? existingLink)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);

        var fileRecords = new Mock<IFileRecordRepository>();
        fileRecords.Setup(x => x.GetByIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var assets = new Mock<IEntityAssetRepository>();
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(existingLink);

        var fileStorage = new Mock<IFileStorageService>();
        fileStorage.Setup(x => x.DeleteAsync(TenantId, UserId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        var handler = new DeleteTaskPendingUploadCommandHandler(currentUser.Object, fileRecords.Object, assets.Object, fileStorage.Object);
        return (handler, fileStorage);
    }

    private static FileRecord Record(Guid uploadedBy) => new()
    {
        Id = FileId, TenantId = TenantId, StorageKey = "k", OriginalFileName = "f.png", SafeFileName = "f.png",
        ContentType = "image/png", FileSizeBytes = 10, ChecksumSha256 = new string('a', 64),
        UploadedByUserId = uploadedBy, Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Handle_NotFound_Returns404()
    {
        var (handler, _) = Build(null, null);
        var result = await handler.Handle(new DeleteTaskPendingUploadCommand(FileId), CancellationToken.None);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NotUploader_Returns403()
    {
        var (handler, _) = Build(Record(Guid.NewGuid()), null);
        var result = await handler.Handle(new DeleteTaskPendingUploadCommand(FileId), CancellationToken.None);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AlreadyLinked_Returns409()
    {
        var link = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = "task", OwnerId = Guid.NewGuid(), AssetPurpose = "task_attachment", FileRecordId = FileId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        var (handler, _) = Build(Record(UserId), link);
        var result = await handler.Handle(new DeleteTaskPendingUploadCommand(FileId), CancellationToken.None);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Handle_UnlinkedOwnUpload_DeletesIt()
    {
        var (handler, fileStorage) = Build(Record(UserId), null);
        var result = await handler.Handle(new DeleteTaskPendingUploadCommand(FileId), CancellationToken.None);
        Assert.True(result.IsSuccess);
        fileStorage.Verify(x => x.DeleteAsync(TenantId, UserId, FileId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
