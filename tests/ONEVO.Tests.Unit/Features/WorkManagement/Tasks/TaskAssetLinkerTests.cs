using Moq;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskAssetLinkerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private (TaskAssetLinker Linker, Mock<IEntityAssetRepository> Assets, Mock<IFileStorageService> FileStorage, Mock<IFileRecordRepository> FileRecords) Build()
    {
        var assets = new Mock<IEntityAssetRepository>();
        var fileStorage = new Mock<IFileStorageService>();
        var fileRecords = new Mock<IFileRecordRepository>();
        var linker = new TaskAssetLinker(assets.Object, fileStorage.Object, fileRecords.Object);
        return (linker, assets, fileStorage, fileRecords);
    }

    private static FileRecord Uploaded(Guid id, Guid uploadedBy, long size = 100) => new()
    {
        Id = id, TenantId = TenantId, StorageKey = "k", OriginalFileName = "f.png", SafeFileName = "f.png",
        ContentType = "image/png", FileSizeBytes = size, ChecksumSha256 = new string('a', 64),
        UploadedByUserId = uploadedBy, Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task SyncAttachmentsAsync_NewFileUploadedByCaller_LinksIt()
    {
        var (linker, assets, _, fileRecords) = Build();
        var fileId = Guid.NewGuid();
        fileRecords.Setup(x => x.GetByIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync(Uploaded(fileId, UserId));
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncAttachmentsAsync(TenantId, UserId, TaskId, new[] { fileId }, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a =>
            a.OwnerType == EntityAssetOwnerTypes.Task && a.OwnerId == TaskId &&
            a.AssetPurpose == UploadPurposeCatalog.TaskAttachment && a.FileRecordId == fileId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncAttachmentsAsync_FileUploadedBySomeoneElse_IsSkipped()
    {
        var (linker, assets, _, fileRecords) = Build();
        var fileId = Guid.NewGuid();
        fileRecords.Setup(x => x.GetByIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync(Uploaded(fileId, Guid.NewGuid()));
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncAttachmentsAsync(TenantId, UserId, TaskId, new[] { fileId }, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAttachmentsAsync_RemovedFromDesiredList_UnlinksAndDeletesFile()
    {
        var (linker, assets, fileStorage, fileRecords) = Build();
        var keptId = Guid.NewGuid();
        var removedId = Guid.NewGuid();
        var removedAsset = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Task, OwnerId = TaskId, AssetPurpose = UploadPurposeCatalog.TaskAttachment, FileRecordId = removedId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile> { new(removedAsset.Id, removedId, "f.png", 100, "image/png", DateTimeOffset.UtcNow, UploadPurposeCatalog.TaskAttachment) });
        assets.Setup(x => x.GetByIdForTenantAsync(TenantId, removedAsset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(removedAsset);
        fileRecords.Setup(x => x.GetByIdAsync(TenantId, keptId, It.IsAny<CancellationToken>())).ReturnsAsync(Uploaded(keptId, UserId));
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, keptId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);

        await linker.SyncAttachmentsAsync(TenantId, UserId, TaskId, new[] { keptId }, CancellationToken.None);

        assets.Verify(x => x.DeleteAsync(It.Is<EntityAsset>(a => a.Id == removedAsset.Id), It.IsAny<CancellationToken>()), Times.Once);
        fileStorage.Verify(x => x.DeleteAsync(TenantId, UserId, removedId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncDescriptionImagesAsync_ExtractsFileIdFromHtml_LinksIt()
    {
        var (linker, assets, _, fileRecords) = Build();
        var fileId = Guid.NewGuid();
        var html = $"<p>See <img src=\"/api/v1/work/tasks/files/{fileId}\"></p>";
        fileRecords.Setup(x => x.GetByIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync(Uploaded(fileId, UserId));
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncDescriptionImagesAsync(TenantId, UserId, TaskId, html, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a => a.AssetPurpose == UploadPurposeCatalog.TaskDescriptionImage && a.FileRecordId == fileId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncDescriptionImagesAsync_NullDescription_UnlinksAllExistingImages()
    {
        var (linker, assets, fileStorage, _) = Build();
        var imageId = Guid.NewGuid();
        var imageAsset = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Task, OwnerId = TaskId, AssetPurpose = UploadPurposeCatalog.TaskDescriptionImage, FileRecordId = imageId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile> { new(imageAsset.Id, imageId, "f.png", 100, "image/png", DateTimeOffset.UtcNow, UploadPurposeCatalog.TaskDescriptionImage) });
        assets.Setup(x => x.GetByIdForTenantAsync(TenantId, imageAsset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(imageAsset);

        await linker.SyncDescriptionImagesAsync(TenantId, UserId, TaskId, null, CancellationToken.None);

        assets.Verify(x => x.DeleteAsync(It.Is<EntityAsset>(a => a.Id == imageAsset.Id), It.IsAny<CancellationToken>()), Times.Once);
        fileStorage.Verify(x => x.DeleteAsync(TenantId, UserId, imageId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
