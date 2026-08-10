using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class LegalEntityLogoCommandHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static LegalEntityEntity Entity(Guid id, Guid? logoFileId = null) => new()
    {
        Id = id,
        TenantId = TenantId,
        Name = "Acme Lanka",
        CountryCode = "LKA",
        CurrencyCode = "LKR",
        IsActive = true,
        LogoFileId = logoFileId
    };

    private void AuthenticateCurrentUser()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(UserId);
    }

    [Fact]
    public async Task SetLogo_ValidRequest_UploadsAndSetsLogoFileId()
    {
        AuthenticateCurrentUser();
        var entity = Entity(Guid.NewGuid());
        var originalName = entity.Name;
        var uploadedFileId = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        _fileStorage.Setup(f => f.UploadAsync(
                TenantId, UserId, "logo.png", "image/png", UploadPurposeCatalog.CompanyLogo, content, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Success(new FileRecordDto(
                uploadedFileId, TenantId, "key", "logo.png", "logo.png", "image/png", 3, new string('a', 64), "PendingScan", DateTimeOffset.UtcNow)));
        var sut = new SetLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);

        var result = await sut.Handle(
            new SetLegalEntityLogoCommand(entity.Id, content, "image/png", "logo.png"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.LogoFileId.Should().Be(uploadedFileId);
        entity.Name.Should().Be(originalName);
        _legalEntities.Verify(r => r.Update(entity), Times.Once);
        _legalEntities.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLogo_EntityNotFound_ReturnsNotFound_AndNeverUploads()
    {
        AuthenticateCurrentUser();
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = new SetLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);
        using var content = new MemoryStream(new byte[] { 1 });

        var result = await sut.Handle(
            new SetLegalEntityLogoCommand(id, content, "image/png", "logo.png"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _legalEntities.Verify(r => r.Update(It.IsAny<LegalEntityEntity>()), Times.Never);
        _fileStorage.Verify(f => f.UploadAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetLogo_UploadRejected_ReturnsUploadFailure_AndDoesNotTouchEntity()
    {
        AuthenticateCurrentUser();
        var entity = Entity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        using var content = new MemoryStream(new byte[] { 1 });
        _fileStorage.Setup(f => f.UploadAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileRecordDto>.Failure("File exceeds the 5 MB limit for company_logo.", 400));
        var sut = new SetLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);

        var result = await sut.Handle(
            new SetLegalEntityLogoCommand(entity.Id, content, "image/png", "logo.png"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        entity.LogoFileId.Should().BeNull();
        _legalEntities.Verify(r => r.Update(It.IsAny<LegalEntityEntity>()), Times.Never);
    }

    [Fact]
    public async Task RemoveLogo_ClearsLogoFileId_AndDoesNotTouchOtherFields()
    {
        AuthenticateCurrentUser();
        var entity = Entity(Guid.NewGuid(), Guid.NewGuid());
        var originalName = entity.Name;
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var sut = new RemoveLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object);

        var result = await sut.Handle(new RemoveLegalEntityLogoCommand(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.LogoFileId.Should().BeNull();
        entity.Name.Should().Be(originalName);
        _legalEntities.Verify(r => r.Update(entity), Times.Once);
        _legalEntities.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveLogo_EntityNotFound_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = new RemoveLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object);

        var result = await sut.Handle(new RemoveLegalEntityLogoCommand(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
