using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.GetPositionById;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using DepartmentEntity = ONEVO.Domain.Features.OrgStructure.Entities.Department;
using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class GetPositionByIdQueryHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IDepartmentRepository> _departmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public GetPositionByIdQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
    }

    private GetPositionByIdQueryHandler CreateHandler()
        => new(_positionsMock.Object, _departmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenPositionDoesNotExist()
    {
        var positionId = Guid.NewGuid();
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ONEVO.Domain.Features.OrgStructure.Entities.Position?)null);

        var result = await CreateHandler().Handle(
            new GetPositionByIdQuery(_legalEntityId, positionId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_ReturnsPositionWithDepartmentAndReportsToNames_WhenBothPresent()
    {
        var departmentId = Guid.NewGuid();
        var reportsToId = Guid.NewGuid();
        var position = new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            DepartmentId = departmentId,
            ReportsToPositionId = reportsToId,
            Name = "Customer Support Manager",
            Code = "CS-MGR",
            IsActive = true
        };
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        _departmentsMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, departmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentEntity { Id = departmentId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Customer Support", IsActive = true });
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, reportsToId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ONEVO.Domain.Features.OrgStructure.Entities.Position { Id = reportsToId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Operations Manager", IsActive = true });
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await CreateHandler().Handle(
            new GetPositionByIdQuery(_legalEntityId, position.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Customer Support", result.Value!.DepartmentName);
        Assert.Equal("Operations Manager", result.Value.ReportsToPositionName);
        Assert.Equal(2, result.Value.ChildCount);
    }

    [Fact]
    public async Task Handle_ReturnsNullNames_WhenDepartmentAndReportsToAreAbsent()
    {
        var position = new ONEVO.Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            LegalEntityId = _legalEntityId,
            DepartmentId = null,
            ReportsToPositionId = null,
            Name = "Founder",
            Code = "FOUNDER",
            IsActive = true
        };
        _positionsMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        _positionsMock
            .Setup(p => p.CountActiveReportsToPositionAsync(_tenantId, _legalEntityId, position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await CreateHandler().Handle(
            new GetPositionByIdQuery(_legalEntityId, position.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DepartmentName);
        Assert.Null(result.Value.ReportsToPositionName);
        _departmentsMock.Verify(
            d => d.GetByIdForLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
