using Moq;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Common;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Tasks;

public class TaskAssetLinkerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    private (TaskAssetLinker Linker, Mock<IEntityAssetRepository> Assets, Mock<IFileStorageService> FileStorage) Build()
    {
        var assets = new Mock<IEntityAssetRepository>();
        var fileStorage = new Mock<IFileStorageService>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var linker = new TaskAssetLinker(assets.Object, fileStorage.Object, unitOfWork.Object);
        return (linker, assets, fileStorage);
    }

    private static FileRecordDto Uploaded(Guid id, Guid uploadedBy, long size = 100) => new(
        id, TenantId, "k", "f.png", "f.png", "image/png", size, new string('a', 64),
        "available", DateTimeOffset.UtcNow, uploadedBy, null);

    [Fact]
    public async Task SyncAttachmentsAsync_NewFileUploadedByCaller_LinksIt()
    {
        var (linker, assets, fileStorage) = Build();
        var fileId = Guid.NewGuid();
        fileStorage.Setup(x => x.GetRecordAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Uploaded(fileId, UserId)));
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
        var (linker, assets, fileStorage) = Build();
        var fileId = Guid.NewGuid();
        fileStorage.Setup(x => x.GetRecordAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Uploaded(fileId, Guid.NewGuid())));
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncAttachmentsAsync(TenantId, UserId, TaskId, new[] { fileId }, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAttachmentsAsync_FileNotFound_IsSkipped()
    {
        var (linker, assets, fileStorage) = Build();
        var fileId = Guid.NewGuid();
        fileStorage.Setup(x => x.GetRecordAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.NotFound("File not found."));
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncAttachmentsAsync(TenantId, UserId, TaskId, new[] { fileId }, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAttachmentsAsync_RemovedFromDesiredList_UnlinksAndDeletesFile()
    {
        var (linker, assets, fileStorage) = Build();
        var keptId = Guid.NewGuid();
        var removedId = Guid.NewGuid();
        var removedAsset = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Task, OwnerId = TaskId, AssetPurpose = UploadPurposeCatalog.TaskAttachment, FileRecordId = removedId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile> { new(removedAsset.Id, removedId, "f.png", 100, "image/png", DateTimeOffset.UtcNow, UploadPurposeCatalog.TaskAttachment) });
        assets.Setup(x => x.GetByIdForTenantAsync(TenantId, removedAsset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(removedAsset);
        fileStorage.Setup(x => x.GetRecordAsync(TenantId, keptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Uploaded(keptId, UserId)));
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, keptId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);

        await linker.SyncAttachmentsAsync(TenantId, UserId, TaskId, new[] { keptId }, CancellationToken.None);

        assets.Verify(x => x.DeleteAsync(It.Is<EntityAsset>(a => a.Id == removedAsset.Id), It.IsAny<CancellationToken>()), Times.Once);
        fileStorage.Verify(x => x.DeleteAsync(TenantId, UserId, removedId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncDescriptionImagesAsync_ExtractsFileIdFromHtml_LinksIt()
    {
        var (linker, assets, fileStorage) = Build();
        var fileId = Guid.NewGuid();
        var html = $"<p>See <img src=\"/api/v1/work/tasks/files/{fileId}\"></p>";
        fileStorage.Setup(x => x.GetRecordAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Uploaded(fileId, UserId)));
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncDescriptionImagesAsync(TenantId, UserId, TaskId, html, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a => a.AssetPurpose == UploadPurposeCatalog.TaskDescriptionImage && a.FileRecordId == fileId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncDescriptionImagesAsync_NullDescription_UnlinksAllExistingImages()
    {
        var (linker, assets, fileStorage) = Build();
        var imageId = Guid.NewGuid();
        var imageAsset = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = EntityAssetOwnerTypes.Task, OwnerId = TaskId, AssetPurpose = UploadPurposeCatalog.TaskDescriptionImage, FileRecordId = imageId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, TaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile> { new(imageAsset.Id, imageId, "f.png", 100, "image/png", DateTimeOffset.UtcNow, UploadPurposeCatalog.TaskDescriptionImage) });
        assets.Setup(x => x.GetByIdForTenantAsync(TenantId, imageAsset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(imageAsset);

        await linker.SyncDescriptionImagesAsync(TenantId, UserId, TaskId, null, CancellationToken.None);

        assets.Verify(x => x.DeleteAsync(It.Is<EntityAsset>(a => a.Id == imageAsset.Id), It.IsAny<CancellationToken>()), Times.Once);
        fileStorage.Verify(x => x.DeleteAsync(TenantId, UserId, imageId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncCommentAttachmentsAsync_NewFileUploadedByCaller_LinksItUnderCommentOwnerType()
    {
        var (linker, assets, fileStorage) = Build();
        var commentId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        fileStorage.Setup(x => x.GetRecordAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Uploaded(fileId, UserId)));
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Comment, commentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncCommentAttachmentsAsync(TenantId, UserId, commentId, new[] { fileId }, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a =>
            a.OwnerType == EntityAssetOwnerTypes.Comment && a.OwnerId == commentId &&
            a.AssetPurpose == UploadPurposeCatalog.CommentAttachment && a.FileRecordId == fileId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncCommentDescriptionImagesAsync_ExtractsFileIdFromHtml_LinksItUnderCommentOwnerType()
    {
        var (linker, assets, fileStorage) = Build();
        var commentId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var html = $"<p>See <img src=\"/api/v1/work/tasks/files/{fileId}\"></p>";
        fileStorage.Setup(x => x.GetRecordAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Uploaded(fileId, UserId)));
        assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, fileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Comment, commentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.SyncCommentDescriptionImagesAsync(TenantId, UserId, commentId, html, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a =>
            a.OwnerType == EntityAssetOwnerTypes.Comment &&
            a.AssetPurpose == UploadPurposeCatalog.CommentDescriptionImage && a.FileRecordId == fileId), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static EntityAssetWithFile TaskAttachmentAsset(Guid fileId, string fileName = "report.pdf", string contentType = "application/pdf") =>
        new(Guid.NewGuid(), fileId, fileName, 500, contentType, DateTimeOffset.UtcNow, UploadPurposeCatalog.TaskAttachment);

    [Fact]
    public async Task CopyAttachmentsAsync_SourceHasAttachment_UploadsANewFileAndLinksItToDestination()
    {
        var (linker, assets, fileStorage) = Build();
        var sourceTaskId = Guid.NewGuid();
        var destinationTaskId = Guid.NewGuid();
        var sourceFileId = Guid.NewGuid();
        var copiedFileId = Guid.NewGuid();

        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, sourceTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile> { TaskAttachmentAsset(sourceFileId) });
        fileStorage.Setup(x => x.OpenReadAsync(TenantId, sourceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(new MemoryStream(new byte[] { 1, 2, 3 }), "application/pdf")));
        fileStorage.Setup(x => x.UploadAsync(
                TenantId, UserId, "report.pdf", "application/pdf", UploadPurposeCatalog.TaskAttachment, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Uploaded(copiedFileId, UserId)));

        await linker.CopyAttachmentsAsync(TenantId, UserId, sourceTaskId, destinationTaskId, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a =>
            a.OwnerType == EntityAssetOwnerTypes.Task && a.OwnerId == destinationTaskId &&
            a.AssetPurpose == UploadPurposeCatalog.TaskAttachment && a.FileRecordId == copiedFileId), It.IsAny<CancellationToken>()), Times.Once);
        // The copy must be a genuinely new file record, never the source's own file id - a file can
        // only ever be linked to one owner, so reusing sourceFileId would silently attach to nothing.
        assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a => a.FileRecordId == sourceFileId), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CopyAttachmentsAsync_NoAttachmentsOnSource_UploadsNothing()
    {
        var (linker, assets, fileStorage) = Build();
        var sourceTaskId = Guid.NewGuid();
        var destinationTaskId = Guid.NewGuid();
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, sourceTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile>());

        await linker.CopyAttachmentsAsync(TenantId, UserId, sourceTaskId, destinationTaskId, CancellationToken.None);

        fileStorage.Verify(x => x.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        assets.Verify(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CopyAttachmentsAsync_SourceFileNoLongerOpens_SkipsItWithoutFailing()
    {
        var (linker, assets, fileStorage) = Build();
        var sourceTaskId = Guid.NewGuid();
        var destinationTaskId = Guid.NewGuid();
        var sourceFileId = Guid.NewGuid();
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, sourceTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile> { TaskAttachmentAsset(sourceFileId) });
        fileStorage.Setup(x => x.OpenReadAsync(TenantId, sourceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.NotFound("File not found."));

        await linker.CopyAttachmentsAsync(TenantId, UserId, sourceTaskId, destinationTaskId, CancellationToken.None);

        assets.Verify(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CopyAttachmentsAsync_IgnoresDescriptionImagePurpose_OnlyCopiesTaskAttachments()
    {
        var (linker, assets, fileStorage) = Build();
        var sourceTaskId = Guid.NewGuid();
        var destinationTaskId = Guid.NewGuid();
        var descriptionImageFileId = Guid.NewGuid();
        var descriptionImageAsset = new EntityAssetWithFile(
            Guid.NewGuid(), descriptionImageFileId, "inline.png", 200, "image/png", DateTimeOffset.UtcNow, UploadPurposeCatalog.TaskDescriptionImage);
        assets.Setup(x => x.ListByOwnerAsync(TenantId, EntityAssetOwnerTypes.Task, sourceTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EntityAssetWithFile> { descriptionImageAsset });

        await linker.CopyAttachmentsAsync(TenantId, UserId, sourceTaskId, destinationTaskId, CancellationToken.None);

        fileStorage.Verify(x => x.OpenReadAsync(TenantId, descriptionImageFileId, It.IsAny<CancellationToken>()), Times.Never);
        assets.Verify(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
