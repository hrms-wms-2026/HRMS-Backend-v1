using Moq;
using ONEVO.Application.Common.Constants;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.DeleteObjectiveAsset;
using ONEVO.Application.Features.WorkManagement.Objectives.Commands.UploadObjectiveAsset;
using ONEVO.Application.Features.WorkManagement.Objectives.Queries.GetObjectiveAssetFile;
using ONEVO.Application.Features.WorkManagement.Objectives.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class ObjectiveAssetHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IObjectiveRepository> _objectives = new();
    private readonly Mock<IModuleReadAccess> _readAccess = new();
    private readonly Mock<IPermissionResolver> _permissions = new();
    private readonly Mock<IEntityAssetRepository> _assets = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private Objective _objective = new()
    {
        Id = ObjectiveId, TenantId = TenantId, ProjectId = Guid.NewGuid(), IsActive = true, Title = "Module",
        OwnerId = Guid.NewGuid(), StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1)
    };

    public ObjectiveAssetHandlersTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);
        _objectives.Setup(x => x.GetByIdForTenantAsync(TenantId, ObjectiveId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _objective);
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        GrantReadAccess(true);
    }

    private void GrantReadAccess(bool canRead) =>
        _readAccess.Setup(x => x.CanReadAsync(TenantId, It.IsAny<Objective>(), EmployeeId, It.IsAny<CancellationToken>())).ReturnsAsync(canRead);

    private ObjectiveAssetAccess Access() => new(
        _currentUser.Object, _identity.Object, _objectives.Object, _readAccess.Object, _permissions.Object);

    private static FileRecordDto Record(string name = "plan.pdf") => new(
        FileId, TenantId, "key", name, name, "application/pdf", 2048, "sha", "available",
        new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero), UserId, null);

    private EntityAsset LinkedAsset(Guid? ownerId = null, string ownerType = EntityAssetOwnerTypes.Objective, Guid? createdBy = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = ownerType, OwnerId = ownerId ?? ObjectiveId,
        AssetPurpose = UploadPurposeCatalog.ObjectiveAsset, FileRecordId = FileId, CreatedById = createdBy ?? UserId
    };

    private UploadObjectiveAssetCommandHandler UploadHandler() => new(Access(), _assets.Object, _fileStorage.Object, _unitOfWork.Object);
    private DeleteObjectiveAssetCommandHandler DeleteHandler() => new(Access(), _assets.Object, _fileStorage.Object, _unitOfWork.Object);
    private GetObjectiveAssetFileQueryHandler FileHandler() => new(Access(), _assets.Object, _fileStorage.Object);

    private static UploadObjectiveAssetCommand UploadCommand() =>
        new(ObjectiveId, "plan.pdf", "application/pdf", new MemoryStream(new byte[] { 1, 2, 3 }));

    // ---- upload ----

    [Fact]
    public async Task Upload_Member_StoresFileUnderObjectivePurposeAndLinksIt()
    {
        _fileStorage.Setup(x => x.UploadAsync(TenantId, UserId, "plan.pdf", "application/pdf", UploadPurposeCatalog.ObjectiveAsset, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Record()));
        EntityAsset? added = null;
        _assets.Setup(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>())).Callback<EntityAsset, CancellationToken>((a, _) => added = a);

        var result = await UploadHandler().Handle(UploadCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(EntityAssetOwnerTypes.Objective, added!.OwnerType);
        Assert.Equal(ObjectiveId, added.OwnerId);
        Assert.Equal(UploadPurposeCatalog.ObjectiveAsset, added.AssetPurpose);
        Assert.Equal(FileId, added.FileRecordId);
        Assert.Equal(UserId, added.CreatedById);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var asset = result.Value!;
        Assert.Equal(added.Id, asset.Id);
        Assert.Equal("plan.pdf", asset.FileName);
        Assert.Equal(2048, asset.SizeBytes);
        Assert.Equal("application/pdf", asset.ContentType);
        Assert.Equal($"/api/v1/work/objectives/{ObjectiveId}/assets/{added.Id}/file", asset.DownloadUrl);
    }

    [Fact]
    public async Task Upload_NotAMember_ForbiddenAndNothingStored()
    {
        GrantReadAccess(false);

        var result = await UploadHandler().Handle(UploadCommand(), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        _fileStorage.Verify(x => x.UploadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_FullAdminWithoutMembership_Allowed()
    {
        GrantReadAccess(false);
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "*" });
        _fileStorage.Setup(x => x.UploadAsync(TenantId, UserId, It.IsAny<string>(), It.IsAny<string>(), UploadPurposeCatalog.ObjectiveAsset, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Record()));

        var result = await UploadHandler().Handle(UploadCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Upload_InactiveObjective_NotFound()
    {
        _objective.IsActive = false;

        var result = await UploadHandler().Handle(UploadCommand(), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Upload_StorageRejectsFile_ReturnsItsErrorAndLinksNothing()
    {
        _fileStorage.Setup(x => x.UploadAsync(TenantId, UserId, It.IsAny<string>(), It.IsAny<string>(), UploadPurposeCatalog.ObjectiveAsset, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Failure("File exceeds the maximum allowed size.", 413));

        var result = await UploadHandler().Handle(UploadCommand(), CancellationToken.None);

        Assert.Equal(413, result.StatusCode);
        _assets.Verify(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- delete ----

    [Fact]
    public async Task Delete_Uploader_RemovesLinkThenFile()
    {
        var asset = LinkedAsset();
        _assets.Setup(x => x.GetByIdForTenantAsync(TenantId, asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var result = await DeleteHandler().Handle(new DeleteObjectiveAssetCommand(ObjectiveId, asset.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _assets.Verify(x => x.DeleteAsync(asset, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _fileStorage.Verify(x => x.DeleteAsync(TenantId, UserId, FileId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_ModuleHeadRemovesSomeoneElsesFile_Allowed()
    {
        _objective.OwnerId = EmployeeId;
        var asset = LinkedAsset(createdBy: Guid.NewGuid());
        _assets.Setup(x => x.GetByIdForTenantAsync(TenantId, asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var result = await DeleteHandler().Handle(new DeleteObjectiveAssetCommand(ObjectiveId, asset.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Delete_OtherMembersFile_Forbidden()
    {
        var asset = LinkedAsset(createdBy: Guid.NewGuid());
        _assets.Setup(x => x.GetByIdForTenantAsync(TenantId, asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var result = await DeleteHandler().Handle(new DeleteObjectiveAssetCommand(ObjectiveId, asset.Id), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        _assets.Verify(x => x.DeleteAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(EntityAssetOwnerTypes.Objective, false)]
    [InlineData(EntityAssetOwnerTypes.Task, true)]
    public async Task Delete_AssetOfAnotherOwner_NotFound(string ownerType, bool sameOwnerId)
    {
        var asset = LinkedAsset(ownerId: sameOwnerId ? ObjectiveId : Guid.NewGuid(), ownerType: ownerType);
        _assets.Setup(x => x.GetByIdForTenantAsync(TenantId, asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var result = await DeleteHandler().Handle(new DeleteObjectiveAssetCommand(ObjectiveId, asset.Id), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
        _assets.Verify(x => x.DeleteAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_NotAMember_Forbidden()
    {
        GrantReadAccess(false);

        var result = await DeleteHandler().Handle(new DeleteObjectiveAssetCommand(ObjectiveId, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    // ---- download ----

    [Fact]
    public async Task File_Member_StreamsItWithTheOriginalName()
    {
        var asset = LinkedAsset();
        _assets.Setup(x => x.GetByIdForTenantAsync(TenantId, asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);
        _fileStorage.Setup(x => x.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(Result<FileRecordDto>.Success(Record()));
        var stream = new MemoryStream();
        _fileStorage.Setup(x => x.OpenReadAsync(TenantId, It.Is<FileRecordDto>(r => r.Id == FileId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "application/pdf")));

        var result = await FileHandler().Handle(new GetObjectiveAssetFileQuery(ObjectiveId, asset.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(stream, result.Value!.Content);
        Assert.Equal("application/pdf", result.Value.ContentType);
        Assert.Equal("plan.pdf", result.Value.FileName);
    }

    [Fact]
    public async Task File_NotAMember_Forbidden()
    {
        GrantReadAccess(false);

        var result = await FileHandler().Handle(new GetObjectiveAssetFileQuery(ObjectiveId, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        _fileStorage.Verify(x => x.OpenReadAsync(It.IsAny<Guid>(), It.IsAny<FileRecordDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task File_AssetOfAnotherObjective_NotFound()
    {
        var asset = LinkedAsset(ownerId: Guid.NewGuid());
        _assets.Setup(x => x.GetByIdForTenantAsync(TenantId, asset.Id, It.IsAny<CancellationToken>())).ReturnsAsync(asset);

        var result = await FileHandler().Handle(new GetObjectiveAssetFileQuery(ObjectiveId, asset.Id), CancellationToken.None);

        Assert.Equal(404, result.StatusCode);
    }
}
