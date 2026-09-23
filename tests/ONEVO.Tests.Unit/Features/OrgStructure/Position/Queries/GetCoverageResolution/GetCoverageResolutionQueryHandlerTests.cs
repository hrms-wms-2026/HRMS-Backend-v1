using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetCoverageResolution;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;
using CoverageRecordEntity = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position.Queries.GetCoverageResolution;

public class GetCoverageResolutionQueryHandlerTests
{
    private readonly Mock<IPositionRepository> _positions = new();
    private readonly Mock<IPositionAssignmentRepository> _assignments = new();
    private readonly Mock<ILegalEntityRepository> _legalEntities = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    private GetCoverageResolutionQueryHandler CreateHandler() =>
        new(_positions.Object, _assignments.Object, _legalEntities.Object, _currentUser.Object);

    private void SetupAuth()
    {
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _currentUser.Setup(u => u.TenantId).Returns(_tenantId);
        _legalEntities.Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
        _positions.Setup(p => p.GetByIdsAsync(_tenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionEntity>());
    }

    [Fact]
    public async Task Handle_Resolves_Single_Holder_Owner_Automatically_Ignoring_ResponsibleEmployeeId()
    {
        SetupAuth();
        var ownerPositionId = Guid.NewGuid();
        var soleHolderId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        _positions.Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, "Department", null, deptId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoverageRecordEntity>
            {
                new() { OwnerPositionId = ownerPositionId, OwnerOrder = 1, ResponsibleEmployeeId = null, CoveredTargetType = "Department", CoveredDepartmentId = deptId, Status = CoverageRecordEntity.StatusActive }
            });
        _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder> { new(soleHolderId, "A", "One", "a@acme.test", null) });

        var result = await CreateHandler().Handle(
            new GetCoverageResolutionQuery(_legalEntityId, "Department", null, deptId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value!.Single().Status.Should().Be("Resolved");
        result.Value!.Single().EmployeeId.Should().Be(soleHolderId);
    }

    [Fact]
    public async Task Handle_Resolves_Pooled_Owner_Via_ResponsibleEmployeeId()
    {
        SetupAuth();
        var ownerPositionId = Guid.NewGuid();
        var chosenId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        _positions.Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, "Department", null, deptId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoverageRecordEntity>
            {
                new() { OwnerPositionId = ownerPositionId, OwnerOrder = 1, ResponsibleEmployeeId = chosenId, CoveredTargetType = "Department", CoveredDepartmentId = deptId, Status = CoverageRecordEntity.StatusActive }
            });
        _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder>
            {
                new(chosenId, "A", "One", "a@acme.test", null),
                new(Guid.NewGuid(), "B", "Two", "b@acme.test", null),
            });

        var result = await CreateHandler().Handle(
            new GetCoverageResolutionQuery(_legalEntityId, "Department", null, deptId), CancellationToken.None);

        result.Value!.Single().Status.Should().Be("Resolved");
        result.Value!.Single().EmployeeId.Should().Be(chosenId);
    }

    [Fact]
    public async Task Handle_Marks_Incomplete_When_ResponsibleEmployeeId_Is_Stale()
    {
        SetupAuth();
        var ownerPositionId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        _positions.Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, "Department", null, deptId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoverageRecordEntity>
            {
                new() { OwnerPositionId = ownerPositionId, OwnerOrder = 1, ResponsibleEmployeeId = staleId, CoveredTargetType = "Department", CoveredDepartmentId = deptId, Status = CoverageRecordEntity.StatusActive }
            });
        _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder>
            {
                new(Guid.NewGuid(), "A", "One", "a@acme.test", null),
                new(Guid.NewGuid(), "B", "Two", "b@acme.test", null),
            });

        var result = await CreateHandler().Handle(
            new GetCoverageResolutionQuery(_legalEntityId, "Department", null, deptId), CancellationToken.None);

        result.Value!.Single().Status.Should().Be("Incomplete");
        result.Value!.Single().EmployeeId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Marks_Incomplete_When_Owner_Position_Is_Vacant()
    {
        SetupAuth();
        var ownerPositionId = Guid.NewGuid();
        var deptId = Guid.NewGuid();

        _positions.Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, "Department", null, deptId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoverageRecordEntity>
            {
                new() { OwnerPositionId = ownerPositionId, OwnerOrder = 1, ResponsibleEmployeeId = null, CoveredTargetType = "Department", CoveredDepartmentId = deptId, Status = CoverageRecordEntity.StatusActive }
            });
        _assignments.Setup(a => a.GetActiveHoldersAsync(_tenantId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder>());

        var result = await CreateHandler().Handle(
            new GetCoverageResolutionQuery(_legalEntityId, "Department", null, deptId), CancellationToken.None);

        result.Value!.Single().Status.Should().Be("Incomplete");
    }
}
