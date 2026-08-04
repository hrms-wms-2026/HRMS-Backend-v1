using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.DeleteLegalEntity;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class DeleteLegalEntityCommandHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private DeleteLegalEntityCommandHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        return new DeleteLegalEntityCommandHandler(_legalEntities.Object, _currentUser.Object);
    }

    private static LegalEntityEntity ActiveEntity(Guid id, string name = "Acme Lanka") => new()
    {
        Id = id,
        TenantId = TenantId,
        Name = name,
        CountryCode = "LKA",
        CurrencyCode = "LKR",
        IsActive = true
    };

    [Fact]
    public async Task Handle_ValidConfirmName_SoftDeactivates_AndDoesNotRemoveRow()
    {
        var entity = ActiveEntity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _legalEntities.Setup(r => r.CountActiveByTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var sut = BuildSut();

        var result = await sut.Handle(new DeleteLegalEntityCommand(entity.Id, "Acme Lanka"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        entity.IsActive.Should().BeFalse();
        _legalEntities.Verify(r => r.Update(entity), Times.Once);
        _legalEntities.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ConfirmNameMismatch_ReturnsFailure_AndDoesNotPersist()
    {
        var entity = ActiveEntity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var sut = BuildSut();

        var result = await sut.Handle(new DeleteLegalEntityCommand(entity.Id, "Wrong Name"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Company name confirmation does not match.");
        entity.IsActive.Should().BeTrue();
        _legalEntities.Verify(r => r.Update(It.IsAny<LegalEntityEntity>()), Times.Never);
        _legalEntities.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_LastActiveCompany_ReturnsFailure_AndDoesNotPersist()
    {
        var entity = ActiveEntity(Guid.NewGuid());
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _legalEntities.Setup(r => r.CountActiveByTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var sut = BuildSut();

        var result = await sut.Handle(new DeleteLegalEntityCommand(entity.Id, "Acme Lanka"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Cannot delete or deactivate the last active company in the tenant.");
        entity.IsActive.Should().BeTrue();
        _legalEntities.Verify(r => r.Update(It.IsAny<LegalEntityEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MissingOrOutOfTenantEntity_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = BuildSut();

        var result = await sut.Handle(new DeleteLegalEntityCommand(id, "Anything"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
