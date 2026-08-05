using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.UpdatePosition;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using DepartmentEntity = ONEVO.Domain.Features.OrgStructure.Entities.Department;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class UpdatePositionCommandHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IDepartmentRepository> _departmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
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
                Name = "Old Name", Code = "OLD-CODE", PositionType = "unique", MaxOccupancy = 1, IsActive = true
            });
        _positionsMock
            .Setup(p => p.ExistsByCodeAsync(_tenantId, _legalEntityId, It.IsAny<string>(), _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.ExistsByNameAsync(_tenantId, _legalEntityId, It.IsAny<string>(), _positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private UpdatePositionCommandHandler CreateHandler()
        => new(_positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

    private UpdatePositionCommand ValidCommand(
        Guid? departmentId = null, string name = "New Name", string code = "NEW-CODE",
        string positionType = "unique", int maxOccupancy = 1, Guid? reportsToPositionId = null)
        => new(_legalEntityId, _positionId, departmentId ?? _departmentId, name, code, positionType, maxOccupancy, reportsToPositionId);

    [Fact]
    public async Task Handle_PreservesTenantAndLegalEntityScope_WhenUpdatingFields()
    {
        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_legalEntityId, result.Value!.LegalEntityId);
        Assert.Equal("New Name", result.Value.Name);
        Assert.Equal("NEW-CODE", result.Value.Code);
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
        Assert.Equal(409, result.StatusCode);
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
        Assert.Equal(409, result.StatusCode);
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
    public async Task Handle_ReturnsConflict_WhenLegalEntityIsInactive()
    {
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = false });

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
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

        var command = new UpdatePositionCommand(_legalEntityId, missingPositionId, _departmentId, "Name", "CODE", "unique", 1, null);

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
}
