using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetActiveHolders;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position.Queries.GetActiveHolders;

public sealed class GetActiveHoldersQueryHandlerTests
{
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private GetActiveHoldersQueryHandler CreateHandler() =>
        new(_assignments.Object, _positions.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_Returns_Holders_From_Repository()
    {
        var tenantId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = positionId, TenantId = tenantId, Name = "Team Lead" });
        _assignments.Setup(a => a.GetActiveHoldersAsync(tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder> { new(Guid.NewGuid(), "Jane", "Doe", "jane@acme.test", null) });

        var result = await CreateHandler().Handle(new GetActiveHoldersQuery(legalEntityId, positionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_Position_Missing()
    {
        var tenantId = Guid.NewGuid();
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _positions.Setup(p => p.GetByIdForLegalEntityAsync(tenantId, It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionEntity?)null);

        var result = await CreateHandler().Handle(new GetActiveHoldersQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
