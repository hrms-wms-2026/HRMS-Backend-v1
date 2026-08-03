using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class LegalEntityLogoCommandHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

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
    public async Task SetLogo_ValidRequest_UpdatesOnlyLogoFileId()
    {
        AuthenticateCurrentUser();
        var entity = Entity(Guid.NewGuid());
        var originalName = entity.Name;
        var fileId = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var sut = new SetLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object);

        var result = await sut.Handle(new SetLegalEntityLogoCommand(entity.Id, fileId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.LogoFileId.Should().Be(fileId);
        entity.Name.Should().Be(originalName);
        _legalEntities.Verify(r => r.Update(entity), Times.Once);
        _legalEntities.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetLogo_EntityNotFound_ReturnsNotFound()
    {
        AuthenticateCurrentUser();
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = new SetLegalEntityLogoCommandHandler(_legalEntities.Object, _currentUser.Object);

        var result = await sut.Handle(new SetLegalEntityLogoCommand(id, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
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
