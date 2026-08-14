using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.CoreHr.PositionAssignment.RepositoryInterfaces;
using ONEVO.Application.Features.OrgStructure.Queries.ListPositions;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using Xunit;

using LegalEntityEntity = ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Position;

public sealed class ListPositionsQueryHandlerTests
{
    private readonly Mock<IPositionRepository> _positionsMock = new();
    private readonly Mock<IPositionAssignmentRepository> _positionAssignmentsMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntitiesMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public ListPositionsQueryHandlerTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LegalEntityEntity { Id = _legalEntityId, TenantId = _tenantId, IsActive = true });
        _positionAssignmentsMock
            .Setup(p => p.GetOccupancyPreviewsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionOccupancyPreview>());
    }

    private ListPositionsQueryHandler CreateHandler()
        => new(_positionsMock.Object, _positionAssignmentsMock.Object, _legalEntitiesMock.Object, _currentUserMock.Object);

    private static ListPositionsQuery DefaultQuery(
        Guid legalEntityId,
        Guid? departmentId = null,
        string? search = null,
        bool includeInactive = false,
        int page = 1,
        int pageSize = 20,
        string sortBy = "name",
        string sortDirection = "asc")
        => new(legalEntityId, departmentId, search, includeInactive, page, pageSize, sortBy, sortDirection);

    [Fact]
    public async Task Handle_PassesTrimmedLowercasedSortAndSearchToRepository()
    {
        _positionsMock
            .Setup(p => p.ListPageAsync(
                _tenantId, _legalEntityId, null, "finance", false, "name", "asc", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionPage([], 0, 1, 20, 0));

        var query = DefaultQuery(_legalEntityId, search: "  finance  ", sortBy: "  Name  ", sortDirection: "ASC");

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _positionsMock.Verify(
            p => p.ListPageAsync(_tenantId, _legalEntityId, null, "finance", false, "name", "asc", 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsPaginationMetadataFromRepository()
    {
        var items = new List<ONEVO.Domain.Features.OrgStructure.Entities.Position>
        {
            new() { Id = Guid.NewGuid(), TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Alpha", Code = "A", IsActive = true }
        };
        _positionsMock
            .Setup(p => p.ListPageAsync(
                _tenantId, _legalEntityId, null, null, false, "name", "asc", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionPage(items, 41, 1, 20, 3));

        var result = await CreateHandler().Handle(DefaultQuery(_legalEntityId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(41, result.Value!.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(20, result.Value.PageSize);
        Assert.Single(result.Value.Items);
        Assert.Equal("Alpha", result.Value.Items[0].Name);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenLegalEntityMissing()
    {
        var missingLegalEntityId = Guid.NewGuid();
        _legalEntitiesMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, missingLegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalEntityEntity?)null);

        var result = await CreateHandler().Handle(DefaultQuery(missingLegalEntityId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_PopulatesOccupantPreviewFields_FromBatchedAssignmentRepository()
    {
        var positionId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var avatarFileId = Guid.NewGuid();
        var items = new List<ONEVO.Domain.Features.OrgStructure.Entities.Position>
        {
            new() { Id = positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Engineer", Code = "ENG", MaxOccupancy = 5, IsActive = true }
        };
        _positionsMock
            .Setup(p => p.ListPageAsync(
                _tenantId, _legalEntityId, null, null, false, "name", "asc", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionPage(items, 1, 1, 20, 1));

        _positionAssignmentsMock
            .Setup(p => p.GetOccupancyPreviewsAsync(
                _tenantId, It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == positionId), 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, PositionOccupancyPreview>
            {
                [positionId] = new PositionOccupancyPreview(
                    3,
                    [new PositionOccupantPreviewItem(employeeId, "Jane", "Smith", avatarFileId)])
            });

        var result = await CreateHandler().Handle(DefaultQuery(_legalEntityId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(3, item.AssignedCount);
        Assert.Equal(2, item.RemainingAssignedCount);
        Assert.Equal(5, item.MaxOccupancy);
        var occupant = Assert.Single(item.OccupantPreview);
        Assert.Equal(employeeId, occupant.EmployeeId);
        Assert.Equal("Jane Smith", occupant.DisplayName);
        Assert.Equal("JS", occupant.Initials);
        Assert.Equal(avatarFileId, occupant.AvatarFileId);
        Assert.Null(occupant.AvatarUrl);
    }

    [Fact]
    public async Task Handle_ReturnsZeroAssignedCountAndEmptyPreview_WhenPositionHasNoAssignments()
    {
        var positionId = Guid.NewGuid();
        var items = new List<ONEVO.Domain.Features.OrgStructure.Entities.Position>
        {
            new() { Id = positionId, TenantId = _tenantId, LegalEntityId = _legalEntityId, Name = "Engineer", Code = "ENG", IsActive = true }
        };
        _positionsMock
            .Setup(p => p.ListPageAsync(
                _tenantId, _legalEntityId, null, null, false, "name", "asc", 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionPage(items, 1, 1, 20, 1));

        var result = await CreateHandler().Handle(DefaultQuery(_legalEntityId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(0, item.AssignedCount);
        Assert.Equal(0, item.RemainingAssignedCount);
        Assert.Empty(item.OccupantPreview);
    }
}
