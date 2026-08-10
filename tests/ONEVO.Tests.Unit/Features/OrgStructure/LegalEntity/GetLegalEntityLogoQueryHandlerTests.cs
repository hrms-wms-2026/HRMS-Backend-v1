using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.DTOs.Responses;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class GetLegalEntityLogoQueryHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IFileStorageService> _fileStorage = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

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
    }

    [Fact]
    public async Task Handle_NoLogoSet_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        var entity = Entity(Guid.NewGuid(), logoFileId: null);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var sut = new GetLegalEntityLogoQueryHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);

        var result = await sut.Handle(new GetLegalEntityLogoQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _fileStorage.Verify(f => f.OpenReadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EntityNotFound_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = new GetLegalEntityLogoQueryHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);

        var result = await sut.Handle(new GetLegalEntityLogoQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_LogoSet_DelegatesToFileStorageWithTheStoredFileId()
    {
        AuthenticateCurrentUser();
        var fileId = Guid.NewGuid();
        var entity = Entity(Guid.NewGuid(), logoFileId: fileId);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        using var stream = new MemoryStream();
        _fileStorage.Setup(f => f.OpenReadAsync(TenantId, fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FileStreamDto>.Success(new FileStreamDto(stream, "image/png")));
        var sut = new GetLegalEntityLogoQueryHandler(_legalEntities.Object, _currentUser.Object, _fileStorage.Object);

        var result = await sut.Handle(new GetLegalEntityLogoQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentType.Should().Be("image/png");
        _fileStorage.Verify(f => f.OpenReadAsync(TenantId, fileId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
