using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.ProjectMembers.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetTaskFile;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;
using ONEVO.Domain.Features.Storage.File.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
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
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ObjectiveId = Guid.NewGuid();

    private Mock<IEntityAssetRepository> _assets = new();
    private Mock<IFileRecordRepository> _fileRecords = new();
    private Mock<IWorkTaskRepository> _tasks = new();
    private Mock<IProjectRepository> _projects = new();
    private Mock<IProjectMemberRepository> _members = new();
    private Mock<IPermissionResolver> _permissions = new();
    private Mock<IFileStorageService> _fileStorage = new();
    private Mock<ICallerIdentityResolver> _identity = new();
    private Mock<ICurrentUser> _currentUser = new();

    private GetTaskFileQueryHandler Build()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(EmployeeId);

        return new GetTaskFileQueryHandler(
            _currentUser.Object, _identity.Object, _assets.Object, _fileRecords.Object, _tasks.Object,
            _projects.Object, _members.Object, _permissions.Object, _fileStorage.Object);
    }

    [Fact]
    public async Task Handle_UnlinkedFileOwnedByCaller_StreamsIt()
    {
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        _fileRecords.Setup(x => x.GetByIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(new FileRecord
        {
            Id = FileId, TenantId = TenantId, StorageKey = "k", OriginalFileName = "a.png", SafeFileName = "a.png",
            ContentType = "image/png", FileSizeBytes = 10, ChecksumSha256 = new string('a', 64),
            UploadedByUserId = UserId, Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow
        });
        _fileStorage.Setup(x => x.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(Stream.Null, "image/png")));

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_UnlinkedFileOwnedBySomeoneElse_ReturnsNotFound()
    {
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync((EntityAsset?)null);
        _fileRecords.Setup(x => x.GetByIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(new FileRecord
        {
            Id = FileId, TenantId = TenantId, StorageKey = "k", OriginalFileName = "a.png", SafeFileName = "a.png",
            ContentType = "image/png", FileSizeBytes = 10, ChecksumSha256 = new string('a', 64),
            UploadedByUserId = Guid.NewGuid(), Status = FileRecordStatus.Available, CreatedAt = DateTimeOffset.UtcNow
        });

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_LinkedToAccessibleTask_StreamsIt()
    {
        var link = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = "task", OwnerId = TaskId, AssetPurpose = "task_attachment", FileRecordId = FileId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(link);
        _tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(new WorkTask
        {
            Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, ShortId = "P-1", Title = "T", CreatedAt = DateTimeOffset.UtcNow
        });
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new Project
        {
            Id = ProjectId, TenantId = TenantId, Identifier = "P", IsActive = true, CreatedAt = DateTimeOffset.UtcNow
        });
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string> { "projects:read" });
        _fileStorage.Setup(x => x.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(Stream.Null, "image/png")));

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_LinkedToInaccessibleTask_ReturnsNotFound()
    {
        var link = new EntityAsset { Id = Guid.NewGuid(), TenantId = TenantId, OwnerType = "task", OwnerId = TaskId, AssetPurpose = "task_attachment", FileRecordId = FileId, CreatedByType = "user", CreatedAt = DateTimeOffset.UtcNow };
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>())).ReturnsAsync(link);
        _tasks.Setup(x => x.GetByIdForTenantAsync(TenantId, TaskId, It.IsAny<CancellationToken>())).ReturnsAsync(new WorkTask
        {
            Id = TaskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = ObjectiveId, ShortId = "P-1", Title = "T", CreatedAt = DateTimeOffset.UtcNow
        });
        _projects.Setup(x => x.GetByIdForTenantAsync(TenantId, ProjectId, It.IsAny<CancellationToken>())).ReturnsAsync(new Project
        {
            Id = ProjectId, TenantId = TenantId, Identifier = "P", IsActive = true, CreatedAt = DateTimeOffset.UtcNow
        });
        _permissions.Setup(x => x.ResolveAsync(UserId, TenantId, null, It.IsAny<CancellationToken>())).ReturnsAsync(new List<string>());
        _members.Setup(x => x.GetActiveObjectiveIdsForEmployeeInProjectAsync(TenantId, ProjectId, EmployeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var result = await Build().Handle(new GetTaskFileQuery(FileId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
