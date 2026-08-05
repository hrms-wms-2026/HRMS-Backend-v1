using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionTree;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class GetPositionTreeQueryHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public GetPositionTreeQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
    }

    private GetPositionTreeQueryHandler CreateHandler()
        => new(_positionsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

    [Fact]
    public async Task Handle_BuildsTreeFromLegalEntityScopedPositions()
    {
        // Cross-tenant/cross-legal-entity exclusion is proven at the repository level
        // (EfPositionRepositoryTests.ListByLegalEntityAsync_ExcludesOtherTenantAndOtherLegalEntityPositions),
        // since ListByLegalEntityAsync is already SQL-side filtered. This test only proves the
        // handler wires the correct scope through to the repository and builds the tree from
        // whatever the repository returns.
        var root = new PositionEntity
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "CEO", Code = "CEO", IsActive = true
        };
        var child = new PositionEntity
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "VP Sales", Code = "VP-SALES",
            ReportsToPositionId = root.Id, IsActive = true
        };
        _positionsMock
            .Setup(p => p.ListByLegalEntityAsync(_tenantId, _legalEntityId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionEntity> { root, child });

        var result = await CreateHandler().Handle(
            new GetPositionTreeQuery(_legalEntityId, IncludeInactive: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("CEO", result.Value[0].Name);
        Assert.Single(result.Value[0].Children);
        Assert.Equal("VP Sales", result.Value[0].Children[0].Name);
        _positionsMock.Verify(
            p => p.ListByLegalEntityAsync(_tenantId, _legalEntityId, false, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenLegalEntityMissing()
    {
        var missingLegalEntityId = Guid.NewGuid();
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, missingLegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);

        var result = await CreateHandler().Handle(
            new GetPositionTreeQuery(missingLegalEntityId, false), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
