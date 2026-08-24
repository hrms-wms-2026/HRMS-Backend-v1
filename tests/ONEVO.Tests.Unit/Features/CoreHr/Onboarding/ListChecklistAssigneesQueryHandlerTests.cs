using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListChecklistAssignees;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class ListChecklistAssigneesQueryHandlerTests
{
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    private ListChecklistAssigneesQueryHandler CreateHandler() =>
        new(_legalEntities.Object, _positions.Object, _assignments.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_Returns_Assignees_Including_UserId()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = legalEntityId, TenantId = tenantId, IsActive = true });
        _positions.Setup(r => r.GetByIdForLegalEntityAsync(tenantId, legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, TenantId = tenantId, LegalEntityId = legalEntityId });
        _assignments.Setup(r => r.GetChecklistAssigneesAsync(tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChecklistAssignee>
            {
                new(employeeId, userId, "Jane Smith", "jane@company.com", null)
            });

        var result = await CreateHandler().Handle(new ListChecklistAssigneesQuery(legalEntityId, positionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].UserId.Should().Be(userId);
        result.Value[0].EmployeeId.Should().Be(employeeId);
        result.Value[0].DisplayName.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task Handle_Returns_Empty_When_Repository_Excludes_Inactive_And_Unseated()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = legalEntityId, TenantId = tenantId });
        _positions.Setup(r => r.GetByIdForLegalEntityAsync(tenantId, legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Position { Id = positionId, TenantId = tenantId, LegalEntityId = legalEntityId });
        _assignments.Setup(r => r.GetChecklistAssigneesAsync(tenantId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChecklistAssignee>());

        var result = await CreateHandler().Handle(new ListChecklistAssigneesQuery(legalEntityId, positionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        _assignments.Verify(r => r.GetChecklistAssigneesAsync(tenantId, positionId, It.IsAny<CancellationToken>()), Times.Once);
        _assignments.Verify(r => r.GetActiveHoldersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_Position_Not_In_LegalEntity()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntity { Id = legalEntityId, TenantId = tenantId });
        _positions.Setup(r => r.GetByIdForLegalEntityAsync(tenantId, legalEntityId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Position?)null);

        var result = await CreateHandler().Handle(new ListChecklistAssigneesQuery(legalEntityId, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _assignments.Verify(r => r.GetChecklistAssigneesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Returns_NotFound_When_LegalEntity_Not_In_Tenant()
    {
        var tenantId = Guid.NewGuid();
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(tenantId);
        _legalEntities.Setup(r => r.GetByIdForTenantAsync(tenantId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntity?)null);

        var result = await CreateHandler().Handle(new ListChecklistAssigneesQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
