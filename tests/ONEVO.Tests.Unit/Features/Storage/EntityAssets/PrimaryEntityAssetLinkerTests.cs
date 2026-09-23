using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.Services;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Domain.Features.Storage.EntityAssets.Entities;

namespace ONEVO.Tests.Unit.Features.Storage.EntityAssets;

public sealed class PrimaryEntityAssetLinkerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid FileId = Guid.NewGuid();
    private readonly Mock<IEntityAssetRepository> _assets = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private PrimaryEntityAssetLinker CreateSut()
    {
        _fileStorage.Setup(x => x.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                FileId, TenantId, "key", "avatar.png", "avatar.png", "image/png", 100,
                "sha", "active", DateTimeOffset.UtcNow, UserId, null)));
        _assets.Setup(x => x.ListByOwnerAsync(TenantId, "employee", OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EntityAssetWithFile>());
        _unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return new PrimaryEntityAssetLinker(_assets.Object, _fileStorage.Object, _unitOfWork.Object);
    }

    [Fact]
    public async Task LinkAsync_ValidPendingImage_AddsPrimaryAssetAndCommits()
    {
        var result = await CreateSut().LinkAsync(
            TenantId, UserId, "employee", OwnerId, "employee_avatar", FileId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(FileId);
        _assets.Verify(x => x.AddAsync(It.Is<EntityAsset>(a =>
            a.TenantId == TenantId &&
            a.OwnerType == "employee" &&
            a.OwnerId == OwnerId &&
            a.AssetPurpose == "employee_avatar" &&
            a.FileRecordId == FileId &&
            a.IsPrimary), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LinkAsync_FileOwnedByAnotherUser_ReturnsNotFoundWithoutAdding()
    {
        var sut = CreateSut();
        _fileStorage.Setup(x => x.GetRecordAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                FileId, TenantId, "key", "avatar.png", "avatar.png", "image/png", 100,
                "sha", "active", DateTimeOffset.UtcNow, Guid.NewGuid(), null)));

        var result = await sut.LinkAsync(
            TenantId, UserId, "employee", OwnerId, "employee_avatar", FileId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _assets.Verify(x => x.AddAsync(It.IsAny<EntityAsset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LinkAsync_FileAlreadyLinkedElsewhere_ReturnsConflict()
    {
        _assets.Setup(x => x.GetByFileRecordIdAsync(TenantId, FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityAsset
            {
                TenantId = TenantId,
                OwnerType = "employee",
                OwnerId = Guid.NewGuid(),
                AssetPurpose = "employee_avatar",
                FileRecordId = FileId,
                IsPrimary = true
            });

        var result = await CreateSut().LinkAsync(
            TenantId, UserId, "employee", OwnerId, "employee_avatar", FileId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }
}
