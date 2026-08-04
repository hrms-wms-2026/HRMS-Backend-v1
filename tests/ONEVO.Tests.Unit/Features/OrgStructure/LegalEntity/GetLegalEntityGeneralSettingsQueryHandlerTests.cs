using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityGeneralSettings;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.LegalEntity;

public class GetLegalEntityGeneralSettingsQueryHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private GetLegalEntityGeneralSettingsQueryHandler BuildSut()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(TenantId);
        return new GetLegalEntityGeneralSettingsQueryHandler(_legalEntities.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Handle_EntityInTenant_ReturnsMappedResponse()
    {
        var entity = new LegalEntityEntity
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Name = "Acme Lanka",
            CountryCode = "LKA",
            CurrencyCode = "LKR",
            IsActive = true
        };
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        var sut = BuildSut();

        var result = await sut.Handle(new GetLegalEntityGeneralSettingsQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(entity.Id);
        result.Value.Status.Should().Be("active");
    }

    [Fact]
    public async Task Handle_EntityBelongsToAnotherTenant_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = BuildSut();

        var result = await sut.Handle(new GetLegalEntityGeneralSettingsQuery(id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
