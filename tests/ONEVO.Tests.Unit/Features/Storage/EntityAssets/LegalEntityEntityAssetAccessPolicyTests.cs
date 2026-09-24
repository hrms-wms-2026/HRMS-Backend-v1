using FluentAssertions;
using Moq;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.EntityAssets.Services;
using Xunit;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.Storage.EntityAssets;

public class LegalEntityEntityAssetAccessPolicyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    [Fact]
    public async Task CanReadAsync_LegalEntityExistsInTenant_ReturnsTrue()
    {
        var legalEntities = new Mock<ILegalEntityRepository>();
        legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = LegalEntityId, TenantId = TenantId, Name = "Acme", CountryCode = "LKA", CurrencyCode = "LKR", IsActive = true });
        var sut = new LegalEntityEntityAssetAccessPolicy(legalEntities.Object);

        var result = await sut.CanReadAsync(TenantId, LegalEntityId, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanReadAsync_LegalEntityNotFoundInTenant_ReturnsFalse()
    {
        var legalEntities = new Mock<ILegalEntityRepository>();
        legalEntities.Setup(r => r.GetByIdForTenantAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);
        var sut = new LegalEntityEntityAssetAccessPolicy(legalEntities.Object);

        var result = await sut.CanReadAsync(TenantId, LegalEntityId, CancellationToken.None);

        result.Should().BeFalse();
    }
}
