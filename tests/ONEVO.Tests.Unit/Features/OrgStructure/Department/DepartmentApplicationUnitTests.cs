using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.Commands.ArchiveDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.CreateDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.RestoreDepartment;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateDepartment;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using ONEVO.Application.Features.OrgStructure.Queries.CheckDepartmentArchiveDependencies;
using ONEVO.Application.Features.OrgStructure.Queries.GetDepartment;
using ONEVO.Application.Features.OrgStructure.Queries.ListDepartments;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Domain.Features.OrgStructure.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.OrgStructure.Department;

public class DepartmentApplicationUnitTests
{
    private readonly Mock<IDepartmentRepository> _departmentRepoMock = new();
    private readonly Mock<ILegalEntityRepository> _legalEntityRepoMock = new();
    private readonly Mock<IPositionRepository> _positionRepoMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();
    private readonly Guid _otherLegalEntityId = Guid.NewGuid();
    private readonly DateTimeOffset _fixedTime = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    public DepartmentApplicationUnitTests()
    {
        _currentUserMock.Setup(c => c.IsAuthenticated).Returns(true);
        _currentUserMock.Setup(c => c.TenantId).Returns(_tenantId);
        _dateTimeProviderMock.Setup(d => d.UtcNow).Returns(_fixedTime);

        _legalEntityRepoMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Domain.Features.OrgStructure.Entities.LegalEntity
            {
                Id = _legalEntityId,
                TenantId = _tenantId,
                Name = "Acme Corp"
            });
    }

    #region ListDepartments

    private static ListDepartmentsQuery DefaultListQuery(
        Guid legalEntityId,
        string? search = null,
        bool includeInactive = false,
        Guid? parentDepartmentId = null,
        string view = "flat",
        string sortBy = "name",
        string sortDirection = "asc",
        int page = 1,
        int pageSize = 25)
    {
        return new ListDepartmentsQuery(
            legalEntityId, search, includeInactive, parentDepartmentId, view, sortBy, sortDirection, page, pageSize);
    }

    [Fact]
    public async Task ListDepartments_FlatView_ReturnsFlatPage_AndNullTree()
    {
        var dept1 = CreateDepartment(_tenantId, _legalEntityId, "Engineering");
        var page = new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department> { dept1 }, 1, 1, 25, 1);

        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, false, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.Flat);
        Assert.Null(result.Value.Tree);
        Assert.Single(result.Value.Flat!.Items);
        Assert.Equal(1, result.Value.Flat.TotalCount);
    }

    [Fact]
    public async Task ListDepartments_ReturnsNotFound_WhenLegalEntityDoesNotExist()
    {
        var invalidLegalEntityId = Guid.NewGuid();
        _legalEntityRepoMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, invalidLegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.LegalEntity?)null);

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(invalidLegalEntityId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ListDepartments_TrimsSearch_BeforePassingToRepository()
    {
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, "engineering", false, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, search: "  engineering  "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, "engineering", false, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListDepartments_TreatsEmptyOrWhitespaceSearch_AsNoSearch(string search)
    {
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, false, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, search: search), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListDepartments_ForwardsParentDepartmentIdToRepository()
    {
        var parentId = Guid.NewGuid();
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, false, parentId, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, parentDepartmentId: parentId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, parentId, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListDepartments_IncludeInactiveTrue_ForwardsToRepository()
    {
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, true, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, includeInactive: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, null, true, null, DepartmentSortBy.Name, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("code", DepartmentSortBy.Code)]
    [InlineData("CREATEDAT", DepartmentSortBy.CreatedAt)]
    [InlineData("updatedAt", DepartmentSortBy.UpdatedAt)]
    [InlineData("name", DepartmentSortBy.Name)]
    public async Task ListDepartments_ParsesSortBy_CaseInsensitively(string sortByInput, DepartmentSortBy expected)
    {
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, false, null, expected, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, sortBy: sortByInput), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, null, expected, SortDirection.Ascending, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("DESC", SortDirection.Descending)]
    [InlineData("asc", SortDirection.Ascending)]
    public async Task ListDepartments_ParsesSortDirection_CaseInsensitively(string input, SortDirection expected)
    {
        _departmentRepoMock
            .Setup(d => d.ListPageByLegalEntityAsync(
                _tenantId, _legalEntityId, null, false, null, DepartmentSortBy.Name, expected, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DepartmentPage(new List<Domain.Features.OrgStructure.Entities.Department>(), 0, 1, 25, 0));

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, sortDirection: input), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, null, DepartmentSortBy.Name, expected, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListDepartments_TreeView_CallsTreeRepositoryMethod_NotPageMethod()
    {
        var parent = CreateDepartment(_tenantId, _legalEntityId, "Parent");
        var child = CreateDepartment(_tenantId, _legalEntityId, "Child");
        child.ParentDepartmentId = parent.Id;

        _departmentRepoMock
            .Setup(d => d.ListForTreeByLegalEntityAsync(_tenantId, _legalEntityId, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Features.OrgStructure.Entities.Department> { parent, child });

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId, view: "tree"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Flat);
        Assert.NotNull(result.Value.Tree);
        Assert.Single(result.Value.Tree!.TreeItems);
        Assert.Single(result.Value.Tree.TreeItems[0].Children);
        _departmentRepoMock.Verify(d => d.ListForTreeByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, It.IsAny<CancellationToken>()), Times.Once);
        _departmentRepoMock.Verify(d => d.ListPageByLegalEntityAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Guid?>(),
            It.IsAny<DepartmentSortBy>(), It.IsAny<SortDirection>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListDepartments_TreeView_IgnoresParentDepartmentIdAndPagination()
    {
        _departmentRepoMock
            .Setup(d => d.ListForTreeByLegalEntityAsync(_tenantId, _legalEntityId, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Features.OrgStructure.Entities.Department>
            {
                CreateDepartment(_tenantId, _legalEntityId, "Root")
            });

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            DefaultListQuery(_legalEntityId, view: "tree", parentDepartmentId: Guid.NewGuid(), page: 2, pageSize: 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Tree!.TreeItems);
        _departmentRepoMock.Verify(d => d.ListForTreeByLegalEntityAsync(
            _tenantId, _legalEntityId, null, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListDepartments_TreeView_DoesNotExposeTenantId()
    {
        _departmentRepoMock
            .Setup(d => d.ListForTreeByLegalEntityAsync(_tenantId, _legalEntityId, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Features.OrgStructure.Entities.Department>
            {
                CreateDepartment(_tenantId, _legalEntityId, "Root")
            });

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        await handler.Handle(DefaultListQuery(_legalEntityId, view: "tree"), CancellationToken.None);

        var properties = typeof(DepartmentTreeNodeResponse).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Equals("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region GetDepartment

    [Fact]
    public async Task GetDepartment_ReturnsOnlySelectedLegalEntityDepartment()
    {
        var dept = CreateDepartment(_tenantId, _legalEntityId, "Engineering");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, dept.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dept);

        var handler = new GetDepartmentQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new GetDepartmentQuery(_legalEntityId, dept.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(dept.Id, result.Value!.Id);
        Assert.Equal("Engineering", result.Value.Name);
    }

    [Fact]
    public async Task GetDepartment_ReturnsNotFound_WhenDepartmentDoesNotExistInLegalEntity()
    {
        var missingDeptId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingDeptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new GetDepartmentQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(new GetDepartmentQuery(_legalEntityId, missingDeptId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    #endregion

    #region CheckDepartmentArchiveDependencies

    [Fact]
    public async Task CheckArchiveDependencies_ReturnsCanArchiveTrue_WhenAllCountsAreZero()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eligible");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.CountActiveChildrenAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _departmentRepoMock
            .Setup(d => d.CountActiveEmployeesAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new CheckDepartmentArchiveDependenciesQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            new CheckDepartmentArchiveDependenciesQuery(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CanArchive);
        Assert.Equal(0, result.Value.Blockers.ActiveSubdepartmentCount);
        Assert.Equal(0, result.Value.Blockers.ActiveEmployeeCount);
        Assert.Equal(0, result.Value.Blockers.ActivePositionCount);
        Assert.False(result.Value.Blockers.IsUsedAsParent);
        Assert.False(result.Value.Blockers.HasActiveEmployees);
        Assert.False(result.Value.Blockers.HasActivePositions);
        Assert.False(result.Value.Blockers.PositionDependencyCheckSupported);
        Assert.Equal(
            "No active employees, positions, or subdepartments are linked to this department.",
            result.Value.Message);
    }

    [Fact]
    public async Task CheckArchiveDependencies_ReturnsCanArchiveFalse_WithExactBlockerCounts()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Blocked");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.CountActiveChildrenAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _departmentRepoMock
            .Setup(d => d.CountActiveEmployeesAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var handler = new CheckDepartmentArchiveDependenciesQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            new CheckDepartmentArchiveDependenciesQuery(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanArchive);
        Assert.Equal(2, result.Value.Blockers.ActiveSubdepartmentCount);
        Assert.Equal(4, result.Value.Blockers.ActiveEmployeeCount);
        Assert.True(result.Value.Blockers.IsUsedAsParent);
        Assert.True(result.Value.Blockers.HasActiveEmployees);
        Assert.Equal(
            "This department cannot be archived yet. Reassign linked subdepartments and employees first.",
            result.Value.Message);
    }

    [Fact]
    public async Task CheckArchiveDependencies_ReturnsNotFound_WhenDepartmentDoesNotExist()
    {
        var missingDeptId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingDeptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new CheckDepartmentArchiveDependenciesQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object);

        var result = await handler.Handle(
            new CheckDepartmentArchiveDependenciesQuery(_legalEntityId, missingDeptId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    #endregion

    #region CreateDepartment

    [Fact]
    public async Task CreateDepartment_Succeeds_WhenInputIsValid_AndUsesInjectedClockForCreatedAt()
    {
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Finance", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "Finance", "FIN", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Finance", result.Value!.Name);
        Assert.Equal("FIN", result.Value.Code);
        Assert.Equal(_legalEntityId, result.Value.LegalEntityId);

        // Verify injected clock UtcNow was used for CreatedAt
        Assert.Equal(_fixedTime, result.Value.CreatedAt);

        _departmentRepoMock.Verify(d => d.AddAsync(It.Is<Domain.Features.OrgStructure.Entities.Department>(
            dept => dept.Name == "Finance" && dept.TenantId == _tenantId && dept.LegalEntityId == _legalEntityId && dept.CreatedAt == _fixedTime), It.IsAny<CancellationToken>()), Times.Once);
        _departmentRepoMock.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateDepartment_RejectsDuplicateNameInSameLegalEntity()
    {
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Engineering", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "Engineering", "ENG", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task CreateDepartment_AllowsSameNameInDifferentLegalEntity()
    {
        _legalEntityRepoMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _otherLegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Domain.Features.OrgStructure.Entities.LegalEntity { Id = _otherLegalEntityId, TenantId = _tenantId, Name = "Other Corp" });

        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _otherLegalEntityId, "Engineering", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_otherLegalEntityId, "Engineering", "ENG", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_otherLegalEntityId, result.Value!.LegalEntityId);
    }

    [Fact]
    public async Task CreateDepartment_RejectsParentFromDifferentLegalEntity()
    {
        var invalidParentId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "DevOps", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, invalidParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "DevOps", "DEV", invalidParentId, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task CreateDepartment_RejectsDuplicateCodeCaseInsensitivelyInSameLegalEntity()
    {
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Operations", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.ExistsByCodeAsync(_tenantId, _legalEntityId, "ops", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "Operations", "ops", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task CreateDepartment_AllowsSameCodeInDifferentLegalEntity()
    {
        _legalEntityRepoMock
            .Setup(l => l.GetByIdForTenantAsync(_tenantId, _otherLegalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Domain.Features.OrgStructure.Entities.LegalEntity { Id = _otherLegalEntityId, TenantId = _tenantId, Name = "Other Corp" });

        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _otherLegalEntityId, "Operations", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.ExistsByCodeAsync(_tenantId, _otherLegalEntityId, "OPS", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_otherLegalEntityId, "Operations", "OPS", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("OPS", result.Value!.Code);
    }

    [Theory]
    [InlineData("bad code")]
    [InlineData("bad@code")]
    [InlineData("this-code-is-way-too-long-for-limit")]
    public void CreateDepartmentCommandValidator_RejectsInvalidCodeCharacters(string invalidCode)
    {
        var validator = new CreateDepartmentCommandValidator();
        var command = new CreateDepartmentCommand(Guid.NewGuid(), "Finance", invalidCode, null, null);

        var validationResult = validator.Validate(command);

        Assert.False(validationResult.IsValid);
        Assert.Contains(validationResult.Errors, e => e.PropertyName == nameof(CreateDepartmentCommand.Code));
    }

    [Fact]
    public async Task CreateDepartment_TrimsCode()
    {
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Finance", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.ExistsByCodeAsync(_tenantId, _legalEntityId, "FIN", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "Finance", "  FIN  ", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("FIN", result.Value!.Code);
    }

    [Fact]
    public async Task CreateDepartment_ConvertsWhitespaceCodeToNull()
    {
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Finance", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "Finance", "   ", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Code);
        _departmentRepoMock.Verify(
            d => d.ExistsByCodeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void CreateDepartmentCommandValidator_RejectsEmptyLegalEntityIdAndName()
    {
        var validator = new CreateDepartmentCommandValidator();

        var invalidCommand = new CreateDepartmentCommand(Guid.Empty, "", "TOO_LONG_CODE_1234567890123456", null, null);
        var validationResult = validator.Validate(invalidCommand);

        Assert.False(validationResult.IsValid);
        Assert.Contains(validationResult.Errors, e => e.PropertyName == nameof(CreateDepartmentCommand.LegalEntityId));
        Assert.Contains(validationResult.Errors, e => e.PropertyName == nameof(CreateDepartmentCommand.Name));
        Assert.Contains(validationResult.Errors, e => e.PropertyName == nameof(CreateDepartmentCommand.Code));
    }

    [Fact]
    public async Task CreateDepartment_RejectsHeadPositionId_HeadAssignmentIsDeferredToUpdate()
    {
        // Documented create-time limitation: a new department has no positions belonging to it
        // yet, so "the position must belong to the same department" cannot be evaluated during
        // create. Per Onexo_Department_Position_User_Journey_Validation.md ("Create the
        // department first and assign its head afterwards, which is recommended"), create
        // rejects any non-null headPositionId outright instead of silently ignoring it or
        // accepting a cross-department position - assignment is only supported via update.
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Finance", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "Finance", "FIN", null, Guid.NewGuid());
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        _departmentRepoMock.Verify(d => d.AddAsync(It.IsAny<Domain.Features.OrgStructure.Entities.Department>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateDepartment_AllowsNullHeadPositionId()
    {
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Finance", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new CreateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new CreateDepartmentCommand(_legalEntityId, "Finance", "FIN", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.HeadPositionId);
    }

    #endregion

    #region UpdateDepartment

    [Fact]
    public async Task UpdateDepartment_Succeeds_ByFetchThenMutate_AndUsesInjectedClockForUpdatedAt()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var existingHeadPositionId = Guid.NewGuid();
        existing.HeadPositionId = existingHeadPositionId;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Engineering Software", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Full-replace semantics: the request re-sends the same headPositionId it already had,
        // so this test does not exercise preservation-on-omit (see
        // UpdateDepartment_OmittingHeadPositionId_ClearsIt for that documented behavior).
        _positionRepoMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existingHeadPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePosition(_tenantId, _legalEntityId, existing.Id, isActive: true));

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Engineering Software", "SWE", null, existingHeadPositionId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Engineering Software", result.Value!.Name);
        Assert.Equal("SWE", result.Value.Code);

        // Verify injected clock UtcNow was used for UpdatedAt
        Assert.Equal(_fixedTime, result.Value.UpdatedAt);

        Assert.Equal(existingHeadPositionId, result.Value.HeadPositionId);

        _departmentRepoMock.Verify(d => d.Update(existing), Times.Once);
        _departmentRepoMock.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateDepartment_OmittingHeadPositionId_ClearsIt()
    {
        // Documented resolution for the omitted-vs-null request-model ambiguity: this API uses
        // full-replace PUT semantics for every other field (ParentDepartmentId is always
        // overwritten, never preserved-if-omitted - see the handler comment above
        // existing.ParentDepartmentId). HeadPositionId follows the same convention, so a plain
        // CreateDepartmentCommand/UpdateDepartmentCommand record cannot distinguish "omitted"
        // from "explicitly null" anyway (Guid? defaults to null either way). Omitted therefore
        // clears any previously-assigned head position, exactly like explicit null does.
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        existing.HeadPositionId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", null, null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.HeadPositionId);
        _positionRepoMock.Verify(
            p => p.GetByIdForLegalEntityAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateDepartment_AcceptsHeadPositionId_WhenPositionBelongsToSameActiveDepartment()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var positionId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionRepoMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePosition(_tenantId, _legalEntityId, existing.Id, isActive: true));

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", null, null, positionId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(positionId, result.Value!.HeadPositionId);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsHeadPositionId_WhenPositionDoesNotExist()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var missingPositionId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionRepoMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingPositionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Position?)null);

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", null, null, missingPositionId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsHeadPositionId_WhenPositionBelongsToAnotherLegalEntity()
    {
        // Unit-level complement to the integration test of the same scenario: pins that the
        // handler passes request.LegalEntityId (route-derived scope) into the repository call,
        // not some other legal entity id, and that a position which only exists under a
        // different legal entity is treated as not found (repo scoping hides it, same as the
        // existing Create_ParentInDifferentLegalEntity_Returns404 precedent for parent department).
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var positionId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionRepoMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _otherLegalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePosition(_tenantId, _otherLegalEntityId, existing.Id, isActive: true));

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", null, null, positionId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
        _positionRepoMock.Verify(
            p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, positionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsHeadPositionId_WhenPositionIsInactive()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var positionId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionRepoMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePosition(_tenantId, _legalEntityId, existing.Id, isActive: false));

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", null, null, positionId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsHeadPositionId_WhenPositionBelongsToAnotherDepartment()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var otherDepartmentId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionRepoMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePosition(_tenantId, _legalEntityId, otherDepartmentId, isActive: true));

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", null, null, positionId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsHeadPositionId_WhenPositionHasNoDepartmentAssigned()
    {
        // Position.DepartmentId is nullable ("transitional nullable for migration safety" per
        // the entity comment). A position with no department yet cannot satisfy the
        // same-department rule, so it is rejected just like a mismatched department.
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var positionId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _positionRepoMock
            .Setup(p => p.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatePosition(_tenantId, _legalEntityId, departmentId: null, isActive: true));

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", null, null, positionId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsDuplicateNameInSameLegalEntity()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Sales", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Sales", "SLS", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsSelfParenting()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", "ENG", existing.Id, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public void UpdateDepartmentCommandValidator_RejectsSelfParenting()
    {
        var validator = new UpdateDepartmentCommandValidator();
        var deptId = Guid.NewGuid();

        var invalidCommand = new UpdateDepartmentCommand(_legalEntityId, deptId, "Engineering", "ENG", deptId, null);
        var validationResult = validator.Validate(invalidCommand);

        Assert.False(validationResult.IsValid);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsDuplicateCodeCaseInsensitivelyExcludingSelf()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.ExistsByCodeAsync(_tenantId, _legalEntityId, "ops", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", "ops", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsInactiveParentDepartment()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var inactiveParent = CreateDepartment(_tenantId, _legalEntityId, "Legacy");
        inactiveParent.IsActive = false;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, inactiveParent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveParent);

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", "ENG", inactiveParent.Id, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task UpdateDepartment_RejectsDescendantParentSelection()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Eng");
        var descendant = CreateDepartment(_tenantId, _legalEntityId, "Eng Sub");
        descendant.ParentDepartmentId = existing.Id;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.ExistsByNameAsync(_tenantId, _legalEntityId, "Eng", existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, descendant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(descendant);
        _departmentRepoMock
            .Setup(d => d.IsDescendantAsync(_tenantId, _legalEntityId, existing.Id, descendant.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new UpdateDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _positionRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new UpdateDepartmentCommand(_legalEntityId, existing.Id, "Eng", "ENG", descendant.Id, null);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    #endregion

    #region ArchiveDepartment

    [Fact]
    public async Task ArchiveDepartment_DeactivatesDepartmentRow_AndUsesInjectedClockForUpdatedAt()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Legacy Ops");
        Assert.True(existing.IsActive);

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.CountActiveChildrenAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _departmentRepoMock
            .Setup(d => d.CountActiveEmployeesAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new ArchiveDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new ArchiveDepartmentCommand(_legalEntityId, existing.Id);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.False(existing.IsActive);

        // Verify injected clock UtcNow was used for UpdatedAt
        Assert.Equal(_fixedTime, existing.UpdatedAt);

        _departmentRepoMock.Verify(d => d.Update(existing), Times.Once);
        _departmentRepoMock.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ArchiveDepartment_ReturnsNotFound_WhenDepartmentDoesNotExist()
    {
        var missingDeptId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingDeptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new ArchiveDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var command = new ArchiveDepartmentCommand(_legalEntityId, missingDeptId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ArchiveDepartment_Blocks_WhenActiveChildDepartmentsExist()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Has Children");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.CountActiveChildrenAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _departmentRepoMock
            .Setup(d => d.CountActiveEmployeesAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = new ArchiveDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new ArchiveDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.True(existing.IsActive);
        _departmentRepoMock.Verify(d => d.Update(It.IsAny<Domain.Features.OrgStructure.Entities.Department>()), Times.Never);
        _departmentRepoMock.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ArchiveDepartment_Blocks_WhenActiveEmployeesExist()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Has Employees");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.CountActiveChildrenAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _departmentRepoMock
            .Setup(d => d.CountActiveEmployeesAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var handler = new ArchiveDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new ArchiveDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.True(existing.IsActive);
    }

    #endregion

    #region RestoreDepartment

    [Fact]
    public async Task RestoreDepartment_Succeeds_ForInactiveDepartmentWithNoParent_AndUsesInjectedClockForUpdatedAt()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Archived Root");
        existing.IsActive = false;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existing.IsActive);
        Assert.Equal(_fixedTime, existing.UpdatedAt);
        _departmentRepoMock.Verify(d => d.Update(existing), Times.Once);
        _departmentRepoMock.Verify(d => d.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreDepartment_DoesNotChangeHeadPositionId()
    {
        var headPositionId = Guid.NewGuid();
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Archived With Head");
        existing.IsActive = false;
        existing.HeadPositionId = headPositionId;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.Equal(headPositionId, existing.HeadPositionId);
    }

    [Fact]
    public async Task RestoreDepartment_Succeeds_ForInactiveDepartmentWithActiveParent()
    {
        var parent = CreateDepartment(_tenantId, _legalEntityId, "Active Parent");
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Archived Child");
        existing.IsActive = false;
        existing.ParentDepartmentId = parent.Id;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, parent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existing.IsActive);
    }

    [Fact]
    public async Task RestoreDepartment_Rejects_WhenParentIsInactive()
    {
        var parent = CreateDepartment(_tenantId, _legalEntityId, "Inactive Parent");
        parent.IsActive = false;
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Archived Child");
        existing.IsActive = false;
        existing.ParentDepartmentId = parent.Id;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, parent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parent);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.False(existing.IsActive);
    }

    [Fact]
    public async Task RestoreDepartment_Rejects_WhenParentIsMissing()
    {
        var missingParentId = Guid.NewGuid();
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Archived Child");
        existing.IsActive = false;
        existing.ParentDepartmentId = missingParentId;

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingParentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task RestoreDepartment_ReturnsNotFound_WhenDepartmentDoesNotExist()
    {
        var missingDeptId = Guid.NewGuid();

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, missingDeptId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Features.OrgStructure.Entities.Department?)null);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, missingDeptId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task RestoreDepartment_IsIdempotent_WhenAlreadyActive()
    {
        var existing = CreateDepartment(_tenantId, _legalEntityId, "Already Active");

        _departmentRepoMock
            .Setup(d => d.GetByIdForLegalEntityAsync(_tenantId, _legalEntityId, existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, _currentUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, existing.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _departmentRepoMock.Verify(d => d.Update(It.IsAny<Domain.Features.OrgStructure.Entities.Department>()), Times.Never);
    }

    [Fact]
    public async Task RestoreDepartment_ReturnsForbidden_WhenUnauthenticated()
    {
        var unauthenticatedUserMock = new Mock<ICurrentUser>();
        unauthenticatedUserMock.Setup(c => c.IsAuthenticated).Returns(false);

        var handler = new RestoreDepartmentCommandHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, unauthenticatedUserMock.Object, _dateTimeProviderMock.Object);

        var result = await handler.Handle(new RestoreDepartmentCommand(_legalEntityId, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    #endregion

    #region CheckDepartmentArchiveDependencies Auth Guard

    [Fact]
    public async Task CheckArchiveDependencies_ReturnsForbidden_WhenUnauthenticated()
    {
        var unauthenticatedUserMock = new Mock<ICurrentUser>();
        unauthenticatedUserMock.Setup(c => c.IsAuthenticated).Returns(false);

        var handler = new CheckDepartmentArchiveDependenciesQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, unauthenticatedUserMock.Object);

        var result = await handler.Handle(
            new CheckDepartmentArchiveDependenciesQuery(_legalEntityId, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    #endregion

    #region Tenant Context Isolation Guard

    [Fact]
    public async Task Handlers_DoNotAcceptTenantIdFromRequestInput_ResolvesFromCurrentUserOnly()
    {
        var unauthenticatedUserMock = new Mock<ICurrentUser>();
        unauthenticatedUserMock.Setup(c => c.IsAuthenticated).Returns(false);

        var handler = new ListDepartmentsQueryHandler(
            _departmentRepoMock.Object, _legalEntityRepoMock.Object, unauthenticatedUserMock.Object);

        var result = await handler.Handle(DefaultListQuery(_legalEntityId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    #endregion

    #region ListDepartmentsQueryValidator

    [Fact]
    public void ListDepartmentsQueryValidator_AcceptsDefaultValues()
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", "name", "asc", 1, 25);

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ListDepartmentsQueryValidator_RejectsPageLessThanOne()
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", "name", "asc", 0, 25);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void ListDepartmentsQueryValidator_RejectsPageSizeOutOfRange(int pageSize)
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", "name", "asc", 1, pageSize);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.PageSize));
    }

    [Fact]
    public void ListDepartmentsQueryValidator_AcceptsPageSizeAtUpperBound()
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", "name", "asc", 1, 100);

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    public void ListDepartmentsQueryValidator_RejectsInvalidSortBy(string sortBy)
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", sortBy, "asc", 1, 25);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.SortBy));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    public void ListDepartmentsQueryValidator_RejectsInvalidSortDirection(string sortDirection)
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, "flat", "name", sortDirection, 1, 25);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.SortDirection));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    public void ListDepartmentsQueryValidator_RejectsInvalidView(string view)
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, view, "name", "asc", 1, 25);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.View));
    }

    [Fact]
    public void ListDepartmentsQueryValidator_RejectsSearchLongerThan100Characters()
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, new string('a', 101), false, null, "flat", "name", "asc", 1, 25);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ListDepartmentsQuery.Search));
    }

    [Theory]
    [InlineData("TREE")]
    [InlineData("tree")]
    [InlineData("Flat")]
    public void ListDepartmentsQueryValidator_AcceptsViewCaseInsensitively(string view)
    {
        var validator = new ListDepartmentsQueryValidator();
        var query = new ListDepartmentsQuery(_legalEntityId, null, false, null, view, "name", "asc", 1, 25);

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }

    #endregion

    private Domain.Features.OrgStructure.Entities.Department CreateDepartment(
        Guid tenantId, Guid legalEntityId, string name)
    {
        return new Domain.Features.OrgStructure.Entities.Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            Name = name,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static Domain.Features.OrgStructure.Entities.Position CreatePosition(
        Guid tenantId, Guid legalEntityId, Guid? departmentId, bool isActive)
    {
        return new Domain.Features.OrgStructure.Entities.Position
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LegalEntityId = legalEntityId,
            DepartmentId = departmentId,
            Name = "Head Position",
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
