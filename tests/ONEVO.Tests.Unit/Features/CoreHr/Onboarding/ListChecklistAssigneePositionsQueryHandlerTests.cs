using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistAssigneePositions;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class ListChecklistAssigneePositionsQueryHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private ListChecklistAssigneePositionsQueryHandler CreateHandler() =>
        new(_legalEntities.Object, _positions.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_Returns_Active_Positions_For_LegalEntity()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = legalEntityId, TenantId = tenantId, IsActive = true });
        _positions.Setup(r => r.ListByLegalEntityAsync(tenantId, legalEntityId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>
            {
                new() { Id = positionId, TenantId = tenantId, LegalEntityId = legalEntityId, Name = "HR Partner" }
            });

        var result = await CreateHandler().Handle(new ListChecklistAssigneePositionsQuery(legalEntityId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].Id.Should().Be(positionId);
        result.Value[0].Name.Should().Be("HR Partner");
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_LegalEntity_Outside_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntity?)null);

        var result = await CreateHandler().Handle(new ListChecklistAssigneePositionsQuery(legalEntityId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _positions.Verify(
            r => r.ListByLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
