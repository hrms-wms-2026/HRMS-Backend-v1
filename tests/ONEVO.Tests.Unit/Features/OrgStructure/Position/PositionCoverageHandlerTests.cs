using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.AddManualCoverageRecord;
using ONEVO.Application.Features.OrgStructure.Commands.RemoveManualCoverageRecord;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateManualCoverageRecord;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionCoverage;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;
using DepartmentEntity = ONEVO.Domain.Features.OrgStructure.Entities.Department;
using CoverageRecordEntity = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class PositionCoverageHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IDepartmentRepository> _departmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    public PositionCoverageHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_now);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = _positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Manager", IsActive = true });
    }

    [Fact]
    public async Task AddManualCoverage_PersistsUnlockedManualRecord_ForPositionTarget()
    {
        var coveredPositionId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, coveredPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = coveredPositionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "IC", IsActive = true });

        CoverageRecordEntity? added = null;
        _positionsMock
            .Setup(p => p.AddManagementCoverageRecordAsync(It.IsAny<CoverageRecordEntity>(), It.IsAny<CancellationToken>()))
            .Callback<CoverageRecordEntity, CancellationToken>((record, _) => added = record)
            .Returns(Task.CompletedTask);

        var handler = new AddManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new AddManualCoverageRecordCommand(_legalEntityId, _positionId, CoverageRecordEntity.TargetPosition, coveredPositionId, null, 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.False(added!.IsLocked);
        Assert.Equal(CoverageRecordEntity.SourceManual, added.Source);
        Assert.Equal(_positionId, added.OwnerPositionId);
        Assert.Equal(coveredPositionId, added.CoveredPositionId);
    }

    [Fact]
    public async Task AddManualCoverage_ReturnsConflict_WhenActiveRecordAlreadyClaimsSameOrderForTarget()
    {
        var coveredPositionId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, coveredPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = coveredPositionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "IC", IsActive = true });
        _positionsMock
            .Setup(p => p.HasActiveCoverageConflictAsync(
                _tenantId, _legalEntityId, CoverageRecordEntity.TargetPosition, coveredPositionId, null, 1,
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new AddManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new AddManualCoverageRecordCommand(_legalEntityId, _positionId, CoverageRecordEntity.TargetPosition, coveredPositionId, null, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _positionsMock.Verify(p => p.AddManagementCoverageRecordAsync(It.IsAny<CoverageRecordEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddManualCoverage_ReturnsUnprocessable_WhenOwnerPositionInactive()
    {
        var coveredPositionId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = _positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Manager", IsActive = false });

        var handler = new AddManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new AddManualCoverageRecordCommand(_legalEntityId, _positionId, CoverageRecordEntity.TargetPosition, coveredPositionId, null, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task AddManualCoverage_ReturnsUnprocessable_WhenCoveredPositionInactive()
    {
        var coveredPositionId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, coveredPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = coveredPositionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "IC", IsActive = false });

        var handler = new AddManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new AddManualCoverageRecordCommand(_legalEntityId, _positionId, CoverageRecordEntity.TargetPosition, coveredPositionId, null, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task AddManualCoverage_ReturnsUnprocessable_WhenCoveredDepartmentInactive()
    {
        var coveredDepartmentId = Guid.NewGuid();
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, coveredDepartmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentEntity { Id = coveredDepartmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Support", IsActive = false });

        var handler = new AddManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new AddManualCoverageRecordCommand(_legalEntityId, _positionId, CoverageRecordEntity.TargetDepartment, null, coveredDepartmentId, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task AddManualCoverage_ReturnsConflict_WhenCoveredPositionIsOwnerItself()
    {
        var handler = new AddManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new AddManualCoverageRecordCommand(_legalEntityId, _positionId, CoverageRecordEntity.TargetPosition, _positionId, null, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _positionsMock.Verify(p => p.AddManagementCoverageRecordAsync(It.IsAny<CoverageRecordEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddManualCoverage_ReturnsNotFound_WhenCoveredPositionMissing()
    {
        var missingPositionId = Guid.NewGuid();

        var handler = new AddManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new AddManualCoverageRecordCommand(_legalEntityId, _positionId, CoverageRecordEntity.TargetPosition, missingPositionId, null, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public void Validator_RejectsPositionTarget_WithoutCoveredPositionId()
    {
        var validator = new AddManualCoverageRecordCommandValidator();

        var result = validator.Validate(new AddManualCoverageRecordCommand(
            _legalEntityId, _positionId, CoverageRecordEntity.TargetPosition, null, null, 1));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsDepartmentTarget_WithCoveredPositionIdSet()
    {
        var validator = new AddManualCoverageRecordCommandValidator();

        var result = validator.Validate(new AddManualCoverageRecordCommand(
            _legalEntityId, _positionId, CoverageRecordEntity.TargetDepartment, Guid.NewGuid(), Guid.NewGuid(), 1));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task RemoveManualCoverage_Removes_WhenUnlocked()
    {
        var coverageId = Guid.NewGuid();
        var record = new CoverageRecordEntity
        {
            Id = coverageId, TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = _positionId,
            IsLocked = false, Source = CoverageRecordEntity.SourceManual
        };
        _positionsMock
            .Setup(p => p.GetCoverageRecordByIdAsync(_tenantId, coverageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = new RemoveManualCoverageRecordCommandHandler(_positionsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new RemoveManualCoverageRecordCommand(_legalEntityId, _positionId, coverageId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(p => p.RemoveCoverageRecord(record), Times.Once);
    }

    [Fact]
    public async Task RemoveManualCoverage_RejectsWithConflict_WhenLocked()
    {
        var coverageId = Guid.NewGuid();
        var record = new CoverageRecordEntity
        {
            Id = coverageId, TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = _positionId,
            IsLocked = true, Source = CoverageRecordEntity.SourceReportingStructure
        };
        _positionsMock
            .Setup(p => p.GetCoverageRecordByIdAsync(_tenantId, coverageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = new RemoveManualCoverageRecordCommandHandler(_positionsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new RemoveManualCoverageRecordCommand(_legalEntityId, _positionId, coverageId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _positionsMock.Verify(p => p.RemoveCoverageRecord(It.IsAny<CoverageRecordEntity>()), Times.Never);
    }

    [Fact]
    public async Task RemoveManualCoverage_ReturnsNotFound_WhenRecordBelongsToAnotherPosition()
    {
        var coverageId = Guid.NewGuid();
        var record = new CoverageRecordEntity
        {
            Id = coverageId, TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = Guid.NewGuid(),
            IsLocked = false, Source = CoverageRecordEntity.SourceManual
        };
        _positionsMock
            .Setup(p => p.GetCoverageRecordByIdAsync(_tenantId, coverageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = new RemoveManualCoverageRecordCommandHandler(_positionsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new RemoveManualCoverageRecordCommand(_legalEntityId, _positionId, coverageId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task UpdateManualCoverage_ChangesOwnerOrder_WhenUnlockedManual()
    {
        var coverageId = Guid.NewGuid();
        var coveredPositionId = Guid.NewGuid();
        var record = new CoverageRecordEntity
        {
            Id = coverageId, TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = _positionId,
            CoveredTargetType = CoverageRecordEntity.TargetPosition, CoveredPositionId = coveredPositionId,
            OwnerOrder = 1, IsLocked = false, Source = CoverageRecordEntity.SourceManual, Status = CoverageRecordEntity.StatusActive
        };
        _positionsMock
            .Setup(p => p.GetCoverageRecordByIdAsync(_tenantId, coverageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = new UpdateManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new UpdateManualCoverageRecordCommand(_legalEntityId, _positionId, coverageId, 3),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.OwnerOrder);
        _positionsMock.Verify(p => p.UpdateCoverageRecord(record), Times.Once);
        Assert.Equal(3, record.OwnerOrder);
        Assert.Equal(_now, record.UpdatedAt);
        Assert.Equal(CoverageRecordEntity.TargetPosition, record.CoveredTargetType);
        Assert.Equal(coveredPositionId, record.CoveredPositionId);
        Assert.Equal(CoverageRecordEntity.SourceManual, record.Source);
        Assert.False(record.IsLocked);
        Assert.Equal(CoverageRecordEntity.StatusActive, record.Status);
    }

    [Fact]
    public async Task UpdateManualCoverage_RejectsWithConflict_WhenLockedReportingStructureRecord()
    {
        var coverageId = Guid.NewGuid();
        var record = new CoverageRecordEntity
        {
            Id = coverageId, TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = _positionId,
            IsLocked = true, Source = CoverageRecordEntity.SourceReportingStructure, OwnerOrder = 1
        };
        _positionsMock
            .Setup(p => p.GetCoverageRecordByIdAsync(_tenantId, coverageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = new UpdateManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new UpdateManualCoverageRecordCommand(_legalEntityId, _positionId, coverageId, 2),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _positionsMock.Verify(p => p.UpdateCoverageRecord(It.IsAny<CoverageRecordEntity>()), Times.Never);
    }

    [Fact]
    public async Task UpdateManualCoverage_RejectsWithConflict_WhenNewOrderAlreadyUsedByAnotherRecord()
    {
        var coverageId = Guid.NewGuid();
        var coveredPositionId = Guid.NewGuid();
        var record = new CoverageRecordEntity
        {
            Id = coverageId, TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = _positionId,
            CoveredTargetType = CoverageRecordEntity.TargetPosition, CoveredPositionId = coveredPositionId,
            OwnerOrder = 2, IsLocked = false, Source = CoverageRecordEntity.SourceManual, Status = CoverageRecordEntity.StatusActive
        };
        _positionsMock
            .Setup(p => p.GetCoverageRecordByIdAsync(_tenantId, coverageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _positionsMock
            .Setup(p => p.HasActiveCoverageConflictAsync(
                _tenantId, _legalEntityId, CoverageRecordEntity.TargetPosition, coveredPositionId, null, 1,
                coverageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new UpdateManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new UpdateManualCoverageRecordCommand(_legalEntityId, _positionId, coverageId, 1),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _positionsMock.Verify(p => p.UpdateCoverageRecord(It.IsAny<CoverageRecordEntity>()), Times.Never);
    }

    [Fact]
    public async Task UpdateManualCoverage_AllowsSubmittingTheRecordsOwnCurrentOwnerOrder()
    {
        var coverageId = Guid.NewGuid();
        var coveredPositionId = Guid.NewGuid();
        var record = new CoverageRecordEntity
        {
            Id = coverageId, TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = _positionId,
            CoveredTargetType = CoverageRecordEntity.TargetPosition, CoveredPositionId = coveredPositionId,
            OwnerOrder = 2, IsLocked = false, Source = CoverageRecordEntity.SourceManual, Status = CoverageRecordEntity.StatusActive
        };
        _positionsMock
            .Setup(p => p.GetCoverageRecordByIdAsync(_tenantId, coverageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        // HasActiveCoverageConflictAsync excludes the current record's own id, so resubmitting its
        // existing order must never be reported as a conflict.
        _positionsMock
            .Setup(p => p.HasActiveCoverageConflictAsync(
                _tenantId, _legalEntityId, CoverageRecordEntity.TargetPosition, coveredPositionId, null, 2,
                coverageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new UpdateManualCoverageRecordCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(
            new UpdateManualCoverageRecordCommand(_legalEntityId, _positionId, coverageId, 2),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.OwnerOrder);
    }

    [Fact]
    public void UpdateValidator_RejectsOwnerOrderLessThanOne()
    {
        var validator = new UpdateManualCoverageRecordCommandValidator();

        var result = validator.Validate(new UpdateManualCoverageRecordCommand(_legalEntityId, _positionId, Guid.NewGuid(), 0));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task GetCoverage_ReturnsRecordsOrderedByOwnerOrder_WithResolvedNames()
    {
        var coveredPositionId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, coveredPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = coveredPositionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "IC", IsActive = true });

        var records = new List<CoverageRecordEntity>
        {
            new()
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = _positionId,
                CoveredTargetType = CoverageRecordEntity.TargetPosition, CoveredPositionId = coveredPositionId, OwnerOrder = 1,
                Source = CoverageRecordEntity.SourceReportingStructure, IsLocked = true
            }
        };
        _positionsMock
            .Setup(p => p.ListCoverageByOwnerPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var handler = new GetPositionCoverageQueryHandler(_positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new GetPositionCoverageQuery(_legalEntityId, _positionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = Assert.Single(result.Value!);
        Assert.Equal("Manager", response.OwnerPositionName);
        Assert.Equal("IC", response.CoveredPositionName);
        Assert.True(response.IsLocked);
    }
}
