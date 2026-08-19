using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetCoverageByTarget;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;
using DepartmentEntity = ONEVO.Domain.Features.OrgStructure.Entities.Department;
using CoverageRecordEntity = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

// Regression coverage for the bug where the "add coverage" UI let a second owner position pick
// "Primary Manager" for a department that another owner already claimed: the modal only knew about
// coverage records owned by whichever position's modal was open, never the other owner's rows. This
// query aggregates active coverage for a covered target across ALL owners so the UI can see the
// true occupied set before submit.
public sealed class GetCoverageByTargetQueryHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IDepartmentRepository> _departmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public GetCoverageByTargetQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
    }

    [Fact]
    public async Task ReturnsCoverageOwnedByDifferentPositions_ForTheSameCoveredDepartment()
    {
        var coveredDepartmentId = Guid.NewGuid();
        var ownerA = Guid.NewGuid();
        var ownerB = Guid.NewGuid();

        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, coveredDepartmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentEntity { Id = coveredDepartmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Unicom-Tic-Incubator", IsActive = true });
        _positionsMock
            .Setup(p => p.GetByIdsAsync(_tenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(ownerA)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PositionEntity>
            {
                new() { Id = ownerA, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "HR Manager-I", IsActive = true },
                new() { Id = ownerB, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "HR Manager-UT", IsActive = true }
            });

        var records = new List<CoverageRecordEntity>
        {
            new()
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = ownerA,
                CoveredTargetType = CoverageRecordEntity.TargetDepartment, CoveredDepartmentId = coveredDepartmentId,
                OwnerOrder = 1, Status = CoverageRecordEntity.StatusActive
            }
        };
        _positionsMock
            .Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, CoverageRecordEntity.TargetDepartment, null, coveredDepartmentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var handler = new GetCoverageByTargetQueryHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            new GetCoverageByTargetQuery(_legalEntityId, CoverageRecordEntity.TargetDepartment, null, coveredDepartmentId, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = Assert.Single(result.Value!);
        // The record was made by ownerA (HR Manager-I) - proves the caller (HR Manager-UT's modal)
        // can see it's occupied even though it doesn't own the rule itself.
        Assert.Equal(ownerA, response.OwnerPositionId);
        Assert.Equal("HR Manager-I", response.OwnerPositionName);
        Assert.Equal("Unicom-Tic-Incubator", response.CoveredDepartmentName);
        Assert.Equal(1, response.OwnerOrder);
    }

    [Fact]
    public async Task ReturnsEmpty_WhenNoActiveCoverageExistsForTarget()
    {
        var coveredDepartmentId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, CoverageRecordEntity.TargetDepartment, null, coveredDepartmentId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoverageRecordEntity>());

        var handler = new GetCoverageByTargetQueryHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            new GetCoverageByTargetQuery(_legalEntityId, CoverageRecordEntity.TargetDepartment, null, coveredDepartmentId, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task PassesExcludingRecordId_ThroughToRepository_ForEditInPlace()
    {
        var coveredDepartmentId = Guid.NewGuid();
        var excludingRecordId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.ListActiveCoverageByCoveredTargetAsync(
                _tenantId, _legalEntityId, CoverageRecordEntity.TargetDepartment, null, coveredDepartmentId, excludingRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CoverageRecordEntity>());

        var handler = new GetCoverageByTargetQueryHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            new GetCoverageByTargetQuery(_legalEntityId, CoverageRecordEntity.TargetDepartment, null, coveredDepartmentId, excludingRecordId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(p => p.ListActiveCoverageByCoveredTargetAsync(
            _tenantId, _legalEntityId, CoverageRecordEntity.TargetDepartment, null, coveredDepartmentId, excludingRecordId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Validator_RejectsPositionTarget_WithoutCoveredPositionId()
    {
        var validator = new GetCoverageByTargetQueryValidator();

        var result = validator.Validate(new GetCoverageByTargetQuery(
            _legalEntityId, CoverageRecordEntity.TargetPosition, null, null, null));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsDepartmentTarget_WithCoveredPositionIdSet()
    {
        var validator = new GetCoverageByTargetQueryValidator();

        var result = validator.Validate(new GetCoverageByTargetQuery(
            _legalEntityId, CoverageRecordEntity.TargetDepartment, Guid.NewGuid(), Guid.NewGuid(), null));

        Assert.False(result.IsValid);
    }
}
