using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetCoverageVisualization;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using CoverageRecordEntity = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position.Queries.GetCoverageVisualization;

public sealed class GetCoverageVisualizationQueryHandlerTests
{
    [Fact]
    public async Task Handle_Expands_Department_Scope_And_Marks_A_Stale_Responsible_Holder_Unresolved()
    {
        var tenantId = Guid.NewGuid();
        var legalEntityId = Guid.NewGuid();
        var ownerPositionId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var childDepartmentId = Guid.NewGuid();
        var coveredPositionId = Guid.NewGuid();
        var coveredEmployeeId = Guid.NewGuid();
        var staleResponsibleId = Guid.NewGuid();
        var coverageId = Guid.NewGuid();

        var positions = new Mock<IPositionRepository>();
        var departments = new Mock<IDepartmentRepository>();
        var legalEntities = new Mock<ILegalEntityRepository>();
        var assignments = new Mock<IPositionAssignmentRepository>();
        var hierarchy = new Mock<IEmployeeHierarchyClosureRepository>();
        var employees = new Mock<IEmployeeRepository>();
        var currentUser = new Mock<ICurrentUser>();

        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.TenantId).Returns(tenantId);
        legalEntities.Setup(repo => repo.GetByIdForTenantAsync(tenantId, legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = legalEntityId, TenantId = tenantId, IsActive = true });
        positions.Setup(repo => repo.GetByIdForLegalEntityAsync(tenantId, legalEntityId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = ownerPositionId, TenantId = tenantId, LegalEntityId = legalEntityId, Name = "HR Manager" });
        positions.Setup(repo => repo.ListByLegalEntityAsync(tenantId, legalEntityId, false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionEntity>
            {
                new() { Id = ownerPositionId, TenantId = tenantId, LegalEntityId = legalEntityId, Name = "HR Manager" },
                new() { Id = coveredPositionId, TenantId = tenantId, LegalEntityId = legalEntityId, DepartmentId = childDepartmentId, Name = "Engineer" }
            });
        positions.Setup(repo => repo.ListCoverageByOwnerPositionAsync(tenantId, legalEntityId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoverageRecordEntity>
            {
                new()
                {
                    Id = coverageId, TenantId = tenantId, LegalEntityId = legalEntityId,
                    OwnerPositionId = ownerPositionId, CoveredTargetType = CoverageRecordEntity.TargetDepartment,
                    CoveredDepartmentId = departmentId, OwnerOrder = 2, Source = CoverageRecordEntity.SourceManual,
                    IsLocked = false, Status = CoverageRecordEntity.StatusActive,
                    ResponsibleEmployeeId = staleResponsibleId
                }
            });
        assignments.Setup(repo => repo.GetActiveHoldersAsync(tenantId, ownerPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionActiveHolder>
            {
                new(Guid.NewGuid(), "A", "One", "a@example.test", null),
                new(Guid.NewGuid(), "B", "Two", "b@example.test", null)
            });
        departments.Setup(repo => repo.GetDescendantDepartmentIdsAsync(
                tenantId, legalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { childDepartmentId });
        employees.Setup(repo => repo.ListActiveEmployeeIdsAsync(
                tenantId, legalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { coveredEmployeeId });
        employees.Setup(repo => repo.ListActiveEmployeeIdsByIdsAsync(
                tenantId, legalEntityId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { coveredEmployeeId });

        var handler = new GetCoverageVisualizationQueryHandler(
            positions.Object, departments.Object, legalEntities.Object, assignments.Object,
            hierarchy.Object, employees.Object, currentUser.Object);
        var result = await handler.Handle(
            new GetCoverageVisualizationQuery(legalEntityId, ownerPositionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CoveredPositionCount.Should().Be(1);
        result.Value.CoveredEmployeeCount.Should().Be(1);
        result.Value.UnresolvedRuleCount.Should().Be(1);
        var rule = result.Value.Rules.Should().ContainSingle().Subject;
        rule.CoverageId.Should().Be(coverageId);
        rule.OwnerOrder.Should().Be(2);
        rule.ResolutionStatus.Should().Be("Unresolved");
        rule.ResolutionReason.Should().NotBeNull();
        rule.CoveredPositionIds.Should().Contain(coveredPositionId);
        rule.CoveredEmployeeIds.Should().Contain(coveredEmployeeId);
    }
}
