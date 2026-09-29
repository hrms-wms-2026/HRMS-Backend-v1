using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Storage.File.Commands.DeleteFile;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public sealed class DeleteFileCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IEntityAssetRepository> _entityAssets = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private DeleteFileCommandHandler CreateHandler()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        return new DeleteFileCommandHandler(_fileStorage.Object, _entityAssets.Object, _currentUser.Object);
    }

    private static FileRecordDto Record(Guid uploader) => new(
        FileId, TenantId, "key", "a.png", "a.png", "image/png", 100, "sha",
        "active", DateTimeOffset.UtcNow, uploader, null);

    [Fact]
    public async Task Handle_CallerIsNotUploader_ReturnsForbidden()
    {
        _fileStorage.Setup(x => x.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Record(Guid.NewGuid())));

        var result = await CreateHandler().Handle(new DeleteFileCommand(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _fileStorage.Verify(x => x.DeleteAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CallerIsUploader_DeletesFile()
    {
        _fileStorage.Setup(x => x.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(Record(UserId)));
        _fileStorage.Setup(x => x.DeleteAsync(TenantId, UserId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await CreateHandler().Handle(new DeleteFileCommand(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_FileIsLinked_ReturnsConflictWithoutDeleting()
    {
        _entityAssets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityAsset { TenantId = TenantId, FileRecordId = FileId });

        var result = await CreateHandler().Handle(new DeleteFileCommand(FileId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _fileStorage.Verify(x => x.DeleteAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
