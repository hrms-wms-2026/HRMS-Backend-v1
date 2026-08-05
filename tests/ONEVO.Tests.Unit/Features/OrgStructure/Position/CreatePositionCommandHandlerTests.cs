using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.CreatePosition;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using DepartmentEntity = ONEVO.Domain.Features.OrgStructure.Entities.Department;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;
using PositionEntity = ONEVO.Domain.Features.OrgStructure.Entities.Position;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class CreatePositionCommandHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IDepartmentRepository> _departmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _departmentId = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    public CreatePositionCommandHandlerTests()
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
            .Setup(p => p.ExistsByCodeAsync(_tenantId, _legalEntityId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionsMock
            .Setup(p => p.ExistsByNameAsync(_tenantId, _legalEntityId, It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    private CreatePositionCommandHandler CreateHandler()
        => new(_positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

    private CreatePositionCommand ValidCommand(
        Guid? departmentId = null, string name = "Customer Support Manager", string code = "CS-MGR",
        string positionType = "unique", int maxOccupancy = 1, Guid? reportsToPositionId = null)
        => new(_legalEntityId, departmentId ?? _departmentId, name, code, positionType, maxOccupancy, reportsToPositionId);

    [Fact]
    public async Task Handle_Succeeds_WithValidLegalEntityAndDepartment()
    {
        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Customer Support Manager", result.Value!.Name);
        Assert.Equal("CS-MGR", result.Value.Code);
        Assert.Equal("Customer Support", result.Value.DepartmentName);
        Assert.Equal(_now, result.Value.CreatedAt);
        _positionsMock.Verify(p => p.AddAsync(It.IsAny<PositionEntity>(), It.IsAny<CancellationToken>()), Times.Once);
        _positionsMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TrimsNameAndCode()
    {
        PositionEntity? added = null;
        _positionsMock
            .Setup(p => p.AddAsync(It.IsAny<PositionEntity>(), It.IsAny<CancellationToken>()))
            .Callback<PositionEntity, CancellationToken>((entity, _) => added = entity)
            .Returns(Task.CompletedTask);

        var command = ValidCommand(name: "  Customer Support Manager  ", code: "  CS-MGR  ");

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Customer Support Manager", added!.Name);
        Assert.Equal("CS-MGR", added.Code);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDepartmentMissing()
    {
        var missingDepartmentId = Guid.NewGuid();
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingDepartmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DepartmentEntity?)null);

        var result = await CreateHandler().Handle(ValidCommand(departmentId: missingDepartmentId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDepartmentBelongsToAnotherLegalEntity()
    {
        // Department repository is itself scoped by legalEntityId, so a department from another
        // legal entity is simply never returned by GetByIdForLegalEntityAsync - the default mock
        // setup (returns null for any unmatched Guid combination) already models this correctly.
        var departmentFromAnotherLegalEntity = Guid.NewGuid();

        var result = await CreateHandler().Handle(ValidCommand(departmentId: departmentFromAnotherLegalEntity), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenCodeAlreadyExists()
    {
        _positionsMock
            .Setup(p => p.ExistsByCodeAsync(_tenantId, _legalEntityId, "CS-MGR", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public void Validator_RejectsEmptyCode()
    {
        var validator = new CreatePositionCommandValidator();

        var emptyResult = validator.Validate(ValidCommand(code: ""));
        var whitespaceResult = validator.Validate(ValidCommand(code: "   "));

        Assert.False(emptyResult.IsValid);
        Assert.False(whitespaceResult.IsValid);
    }

    [Fact]
    public void Validator_RejectsInvalidCodeCharacters()
    {
        var validator = new CreatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(code: "CS MGR!"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsInvalidPositionType()
    {
        var validator = new CreatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(positionType: "manager"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsSingleOccupancyCapacityOtherThanOne()
    {
        var validator = new CreatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(positionType: "unique", maxOccupancy: 2));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_AllowsMultiOccupancyCapacityGreaterThanOne()
    {
        var validator = new CreatePositionCommandValidator();

        var result = validator.Validate(ValidCommand(positionType: "pooled", maxOccupancy: 5));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenReportsToPositionBelongsToAnotherLegalEntity()
    {
        var reportsToId = Guid.NewGuid();
        // GetByIdForLegalEntityAsync default mock setup returns null for unmatched combinations,
        // modeling a reports-to position that belongs to a different legal entity.

        var result = await CreateHandler().Handle(ValidCommand(reportsToPositionId: reportsToId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
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
        _departmentsMock.Verify(
            d => d.GetByIdForLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _positionsMock.Verify(p => p.AddAsync(It.IsAny<PositionEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _positionsMock.Verify(p => p.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsConflict_WhenReportsToPositionIsInactive()
    {
        var reportsToId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, reportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionEntity
            {
                Id = reportsToId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Inactive Manager", IsActive = false
            });

        var result = await CreateHandler().Handle(ValidCommand(reportsToPositionId: reportsToId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }
}
