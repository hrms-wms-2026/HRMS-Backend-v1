using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.ArchivePosition;
using ONEVO.Application.Features.OrgStructure.Commands.CheckPositionArchive;
using ONEVO.Application.Features.OrgStructure.Commands.RestorePosition;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using DepartmentEntity = ONEVO.Domain.Features.OrgStructure.Entities.Department;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class ArchiveRestoreCheckPositionCommandHandlerTests
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

    public ArchiveRestoreCheckPositionCommandHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_now);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
    }

    private PositionEntity CreatePositionEntity(bool isActive, Guid? departmentId = null, Guid? reportsToPositionId = null)
    {
        return new PositionEntity
        {
            Id = _positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, DepartmentId = departmentId,
            ReportsToPositionId = reportsToPositionId, Name = "Manager", Code = "MGR", IsActive = isActive
        };
    }

    [Fact]
    public async Task Archive_Blocks_WhenActiveChildPositionsExist()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _positionsMock
            .Setup(p => p.CountHeadDepartmentReferencesAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new ArchivePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new ArchivePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _positionsMock.Verify(p => p.Update(It.IsAny<PositionEntity>()), Times.Never);
    }

    [Fact]
    public async Task Archive_Blocks_WhenReferencedAsDepartmentHead()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _positionsMock
            .Setup(p => p.CountHeadDepartmentReferencesAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new ArchivePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new ArchivePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task Archive_DoesNotReparentChildren_WhenBlocked()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _positionsMock
            .Setup(p => p.CountHeadDepartmentReferencesAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new ArchivePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        await handler.Handle(new ArchivePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        // A blocked archive must touch nothing: no Update on the target or any other position
        // (i.e. no silent reparenting of children), and no SaveChangesAsync call at all.
        _positionsMock.Verify(p => p.Update(It.IsAny<PositionEntity>()), Times.Never);
        _positionsMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Archive_Succeeds_WhenNoBlockers()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _positionsMock
            .Setup(p => p.CountHeadDepartmentReferencesAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new ArchivePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new ArchivePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        _positionsMock.Verify(p => p.Update(It.Is<PositionEntity>(pos => !pos.IsActive)), Times.Once);
    }

    [Fact]
    public async Task Restore_Blocks_WhenDepartmentInactive()
    {
        var departmentId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: false, departmentId: departmentId));
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentEntity { Id = departmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Ops", IsActive = false });

        var handler = new RestorePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestorePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _positionsMock.Verify(p => p.Update(It.IsAny<PositionEntity>()), Times.Never);
    }

    [Fact]
    public async Task Restore_Succeeds_WhenDepartmentActiveAndNoReportsTo()
    {
        var departmentId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: false, departmentId: departmentId));
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentEntity { Id = departmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Ops", IsActive = true });

        var handler = new RestorePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestorePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(p => p.Update(It.Is<PositionEntity>(pos => pos.IsActive)), Times.Once);
    }

    [Fact]
    public async Task Restore_IsIdempotent_WhenAlreadyActive()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));

        var handler = new RestorePositionCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestorePositionCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(p => p.Update(It.IsAny<PositionEntity>()), Times.Never);
    }

    [Fact]
    public async Task CheckArchive_ReturnsExactBlockerCounts_AndFlagsOccupantsUnsupported()
    {
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePositionEntity(isActive: true));
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _positionsMock
            .Setup(p => p.CountHeadDepartmentReferencesAsync(_tenantId, _legalEntityId, _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new CheckPositionArchiveCommandHandler(
            _positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new CheckPositionArchiveCommand(_legalEntityId, _positionId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.ActiveChildPositions);
        Assert.Equal(1, result.Value.HeadOfDepartments);
        Assert.Null(result.Value.ActiveOccupants);
        Assert.False(result.Value.ActiveOccupantsCheckSupported);
        Assert.False(result.Value.CanArchive);
    }
}
