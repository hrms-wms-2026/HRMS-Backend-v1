using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Queries.GetFile;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public sealed class GetFileQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private readonly Mock<IEntityAssetRepository> _entityAssets = new();
    private readonly Mock<IEntityAssetAccessPolicyResolver> _policies = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private GetFileQueryHandler CreateHandler()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new GetFileQueryHandler(
            _entityAssets.Object, _policies.Object, _fileStorage.Object, _currentUser.Object);
    }

    private static FileRecordDto Record(Guid uploader) => new(
        FileId, TenantId, "key", "a.png", "a.png", "image/png", 100, "sha",
        "active", DateTimeOffset.UtcNow, uploader, null);

    [Fact]
    public async Task Handle_UnlinkedFile_UploaderCanReadIt()
    {
        _entityAssets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityAsset?)null);
        _fileStorage.Setup(x => x.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Record(UserId)));
        using var stream = new MemoryStream();
        _fileStorage.Setup(x => x.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "image/png")));

        var result = await CreateHandler().Handle(new GetFileQuery(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UnlinkedFile_OtherUserGetsNotFound()
    {
        _entityAssets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EntityAsset?)null);
        _fileStorage.Setup(x => x.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Record(Guid.NewGuid())));

        var result = await CreateHandler().Handle(new GetFileQuery(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_LinkedFile_UnregisteredOwnerType_DefaultDenies()
    {
        _entityAssets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityAsset
            {
                TenantId = TenantId, OwnerType = "unknown", OwnerId = OwnerId, FileRecordId = FileId
            });

        var result = await CreateHandler().Handle(new GetFileQuery(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_LinkedFile_PolicyDenies_ReturnsNotFound()
    {
        var policy = new Mock<IEntityAssetAccessPolicy>();
        policy.Setup(x => x.CanReadAsync(TenantId, OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _policies.Setup(x => x.Resolve("employee")).Returns(policy.Object);
        _entityAssets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityAsset
            {
                TenantId = TenantId, OwnerType = "employee", OwnerId = OwnerId, FileRecordId = FileId
            });

        var result = await CreateHandler().Handle(new GetFileQuery(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_LinkedFile_PolicyAllows_StreamsFile()
    {
        var policy = new Mock<IEntityAssetAccessPolicy>();
        policy.Setup(x => x.CanReadAsync(TenantId, OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _policies.Setup(x => x.Resolve("employee")).Returns(policy.Object);
        _entityAssets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityAsset
            {
                TenantId = TenantId, OwnerType = "employee", OwnerId = OwnerId, FileRecordId = FileId
            });
        using var stream = new MemoryStream();
        _fileStorage.Setup(x => x.OpenReadAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "image/png")));

        var result = await CreateHandler().Handle(new GetFileQuery(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
