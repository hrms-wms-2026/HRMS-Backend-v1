using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeHierarchyClosure.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;
using ONEVO.Application.Features.OrgStructure.OutboxPayloads;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using DepartmentEntity = ONEVO.Domain.Features.OrgStructure.Entities.Department;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;
using ReportingHistoryEntity = ONEVO.Domain.Features.OrgStructure.Entities.PositionReportingHistory;
using CoverageRecordEntity = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class UpdatePositionCommandHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IDepartmentRepository> _departmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Mock<IOutboxWriter> _outboxWriterMock = new();
    private readonly Mock<IEmployeeHierarchyClosureRepository> _closureRepositoryMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();
    private readonly Guid _positionId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    public UpdatePositionCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_now);
        _dateTimeProviderMock.Setup(d => d.Today).Returns(DateOnly.FromDateTime(_now.UtcDateTime));
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentEntity { Id = _departmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Customer Support", IsActive = true });
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity
            {
                Id = _positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, DepartmentId = _departmentId,
                Name = "Old Name", Code = "OLDCD", PositionType = "unique", MaxOccupancy = 1, IsActive = true
            });
        _positionsMock
            .Setup(p => p.ExistsByCodeAsync(_tenantId, _legalEntityId, It.IsAny<string>(), _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.ExistsByNameAsync(_tenantId, _legalEntityId, It.IsAny<string>(), _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private UpdatePositionCommandHandler CreateHandler()
        => new(_positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object, _outboxWriterMock.Object, _closureRepositoryMock.Object);

    private UpdatePositionCommand ValidCommand(
        Guid? departmentId = null, string name = "New Name", string code = "NEWCD",
        int maxOccupancy = 1, Guid? reportsToPositionId = null)
        => new(_legalEntityId, _positionId, departmentId ?? _departmentId, name, code, maxOccupancy, reportsToPositionId);

    [Fact]
    public async Task Handle_PreservesTenantAndLegalEntityScope_WhenUpdatingFields()
    {
        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_legalEntityId, result.Value!.LegalEntityId);
        Assert.Equal("New Name", result.Value.Name);
        Assert.Equal("NEWCD", result.Value.Code);
        _positionsMock.Verify(
            p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_RejectsReportingToItself()
    {
        var result = await CreateHandler().Handle(
            ValidCommand(reportsToPositionId: _positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_RejectsReportingCycle_WhenNewReportsToIsADescendant()
    {
        var descendantId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, descendantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity
            {
                Id = descendantId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Descendant", IsActive = true
            });
        _positionsMock
            .Setup(p => p.IsDescendantAsync(_tenantId, _legalEntityId, _positionId, descendantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(
            ValidCommand(reportsToPositionId: descendantId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Handle_AllowsReportsToChange_WhenNotADescendant()
    {
        var newParentId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, newParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity
            {
                Id = newParentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "New Manager", IsActive = true
            });
        _positionsMock
            .Setup(p => p.IsDescendantAsync(_tenantId, _legalEntityId, _positionId, newParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await CreateHandler().Handle(
            ValidCommand(reportsToPositionId: newParentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newParentId, result.Value!.ReportsToPositionId);
        Assert.Equal("New Manager", result.Value.ReportsToPositionName);
    }

    [Fact]
    public async Task Handle_AllowsPooledPositionAsReportsToTarget()
    {
        var reportsToId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, reportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity
            {
                Id = reportsToId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Support Pool",
                PositionType = PositionEntity.TypePooled, MaxOccupancy = 5, IsActive = true
            });
        _positionsMock
            .Setup(p => p.IsDescendantAsync(_tenantId, _legalEntityId, _positionId, reportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await CreateHandler().Handle(
            ValidCommand(reportsToPositionId: reportsToId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(reportsToId, result.Value!.ReportsToPositionId);
        Assert.Equal("Support Pool", result.Value.ReportsToPositionName);
        _positionsMock.Verify(p => p.Update(It.IsAny<PositionEntity>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsUnprocessableEntity_WhenLegalEntityIsInactive()
    {
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = false });

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        _positionsMock.Verify(
            p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()),
            Times.Never);
        _departmentsMock.Verify(
            d => d.GetByIdForLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _positionsMock.Verify(p => p.Update(It.IsAny<PositionEntity>()), Times.Never);
        _positionsMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenPositionMissing()
    {
        var missingPositionId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionEntity?)null);

        var command = new UpdatePositionCommand(_legalEntityId, missingPositionId, _departmentId, "Name", "CODE", 1, null);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public void Validator_RejectsEmptyCode()
    {
        var validator = new UpdatePositionCommandValidator();

        var emptyResult = validator.Validate(ValidCommand(code: ""));
        var whitespaceResult = validator.Validate(ValidCommand(code: "   "));

        Assert.False(emptyResult.IsValid);
        Assert.False(whitespaceResult.IsValid);
    }

    [Fact]
    public void Validator_RejectsCodeLongerThanFiveCharacters()
    {
        var validator = new UpdatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(code: "ABCDEF"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_AllowsCodeAtFiveCharacters()
    {
        var validator = new UpdatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(code: "ABCDE"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validator_RejectsMaxOccupancyBelowOne(int maxOccupancy)
    {
        var validator = new UpdatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(maxOccupancy: maxOccupancy));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Handle_DerivesUniquePositionType_WhenMaxOccupancyIsOne()
    {
        var result = await CreateHandler().Handle(ValidCommand(maxOccupancy: 1), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PositionEntity.TypeUnique, result.Value!.PositionType);
    }

    [Fact]
    public async Task Handle_DerivesPooledPositionType_WhenMaxOccupancyGreaterThanOne()
    {
        var result = await CreateHandler().Handle(ValidCommand(maxOccupancy: 3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PositionEntity.TypePooled, result.Value!.PositionType);
    }

    [Fact]
    public async Task Handle_ReplacesLockedCoverageAndClosesHistory_WhenReportsToChangesAcrossDays()
    {
        var oldReportsToId = Guid.NewGuid();
        var newReportsToId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity
            {
                Id = _positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, DepartmentId = _departmentId,
                Name = "Old Name", Code = "OLDCD", PositionType = "unique", MaxOccupancy = 1, IsActive = true,
                ReportsToPositionId = oldReportsToId
            });
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, newReportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = newReportsToId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "New Manager", IsActive = true });
        _positionsMock
            .Setup(p => p.IsDescendantAsync(_tenantId, _legalEntityId, _positionId, newReportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var openHistory = new ReportingHistoryEntity
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, PositionId = _positionId, ReportsToPositionId = oldReportsToId,
            EffectiveFrom = DateOnly.FromDateTime(_now.UtcDateTime).AddDays(-5), EffectiveTo = null
        };
        _positionsMock
            .Setup(p => p.GetCurrentReportingHistoryAsync(_tenantId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openHistory);

        var oldCoverage = new CoverageRecordEntity
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, OwnerPositionId = oldReportsToId,
            CoveredPositionId = _positionId, IsLocked = true, Source = CoverageRecordEntity.SourceReportingStructure
        };
        _positionsMock
            .Setup(p => p.GetLockedReportingStructureCoverageAsync(_tenantId, oldReportsToId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldCoverage);

        var result = await CreateHandler().Handle(ValidCommand(reportsToPositionId: newReportsToId), CancellationToken.None);

        Assert.True(result.IsSuccess);

        _positionsMock.Verify(p => p.UpdateReportingHistory(
            It.Is<ReportingHistoryEntity>(h => h.Id == openHistory.Id && h.EffectiveTo == openHistory.EffectiveFrom.AddDays(4))), Times.Once);
        _positionsMock.Verify(p => p.AddReportingHistoryAsync(
            It.Is<ReportingHistoryEntity>(h => h.ReportsToPositionId == newReportsToId && h.EffectiveTo == null), It.IsAny<CancellationToken>()), Times.Once);
        _positionsMock.Verify(p => p.RemoveCoverageRecord(oldCoverage), Times.Once);
        _positionsMock.Verify(p => p.AddManagementCoverageRecordAsync(
            It.Is<CoverageRecordEntity>(c => c.OwnerPositionId == newReportsToId && c.CoveredPositionId == _positionId && c.IsLocked), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SkipsReportingStructureCoverageSync_WhenManualPrimaryAlreadyClaimsCoveredPosition()
    {
        // A manual coverage rule already claims order 1 (Primary Manager) for this covered
        // position from a different owner. The reporting-structure sync must not attempt a second
        // active order-1 row on top of it - that would violate the covered-target uniqueness
        // constraint - but the position's own reports-to update must still succeed.
        var newReportsToId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, newReportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = newReportsToId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "New Manager", IsActive = true });
        _positionsMock
            .Setup(p => p.IsDescendantAsync(_tenantId, _legalEntityId, _positionId, newReportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _positionsMock
            .Setup(p => p.HasActiveCoverageConflictAsync(
                _tenantId, _legalEntityId, CoverageRecordEntity.TargetPosition, _positionId, null, 1,
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(ValidCommand(reportsToPositionId: newReportsToId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(p => p.AddManagementCoverageRecordAsync(It.IsAny<CoverageRecordEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MutatesOpenHistoryInPlace_WhenReportsToChangesOnSameDay()
    {
        var newReportsToId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, newReportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity { Id = newReportsToId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "New Manager", IsActive = true });
        _positionsMock
            .Setup(p => p.IsDescendantAsync(_tenantId, _legalEntityId, _positionId, newReportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var openHistory = new ReportingHistoryEntity
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, PositionId = _positionId, ReportsToPositionId = null,
            EffectiveFrom = DateOnly.FromDateTime(_now.UtcDateTime), EffectiveTo = null
        };
        _positionsMock
            .Setup(p => p.GetCurrentReportingHistoryAsync(_tenantId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openHistory);

        var result = await CreateHandler().Handle(ValidCommand(reportsToPositionId: newReportsToId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(p => p.UpdateReportingHistory(
            It.Is<ReportingHistoryEntity>(h => h.Id == openHistory.Id && h.ReportsToPositionId == newReportsToId && h.EffectiveTo == null)), Times.Once);
        _positionsMock.Verify(p => p.AddReportingHistoryAsync(It.IsAny<ReportingHistoryEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DoesNotTouchHistoryOrCoverage_WhenReportsToUnchanged()
    {
        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(p => p.GetCurrentReportingHistoryAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _positionsMock.Verify(p => p.AddReportingHistoryAsync(It.IsAny<ReportingHistoryEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _positionsMock.Verify(p => p.AddManagementCoverageRecordAsync(It.IsAny<CoverageRecordEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _positionsMock.Verify(p => p.RemoveCoverageRecord(It.IsAny<CoverageRecordEntity>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EnqueuesPositionUpdatedOutboxMessage()
    {
        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _outboxWriterMock.Verify(
            o => o.EnqueueAsync(
                OutboxMessageTypes.PositionUpdated,
                It.Is<PositionOutboxPayload>(p => p.PositionId == _positionId && p.TenantId == _tenantId),
                _tenantId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_Calls_RebuildAsync_When_ReportsToPositionId_Changes()
    {
        var newParentId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity
            {
                Id = _positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, DepartmentId = _departmentId,
                Name = "Old Name", Code = "OLDCD", PositionType = "unique", MaxOccupancy = 1, IsActive = true,
                ReportsToPositionId = null,
            });
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, newParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity
            {
                Id = newParentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "New Manager", IsActive = true
            });
        _positionsMock
            .Setup(p => p.IsDescendantAsync(_tenantId, _legalEntityId, _positionId, newParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        await CreateHandler().Handle(ValidCommand(reportsToPositionId: newParentId), CancellationToken.None);

        _closureRepositoryMock.Verify(c => c.RebuildAsync(_tenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Does_Not_Call_RebuildAsync_When_ReportsToPositionId_Unchanged()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity
            {
                Id = _positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, DepartmentId = _departmentId,
                Name = "Old Name", Code = "OLDCD", PositionType = "unique", MaxOccupancy = 1, IsActive = true,
                ReportsToPositionId = null,
            });

        await CreateHandler().Handle(ValidCommand(reportsToPositionId: null), CancellationToken.None);

        _closureRepositoryMock.Verify(c => c.RebuildAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
