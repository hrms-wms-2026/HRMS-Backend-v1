using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class ListEmployeesQueryHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IEmployeeAuthorityResolver> _authorityResolver = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _defaultLegalEntityId = Guid.NewGuid();

    public ListEmployeesQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);

        // By default the actor has exactly one Employee row - GetDefaultForUserAsync resolves the
        // implicit legal entity when the query itself doesn't name one (see ListEmployeesQuery.LegalEntityId).
        _employeeRepository
            .Setup(r => r.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = Guid.NewGuid(), TenantId = _tenantId, UserId = _userId, LegalEntityId = _defaultLegalEntityId });

        _authorityResolver
            .Setup(r => r.ResolveVisibilityAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeAuthorityVisibilityRequest req, CancellationToken _) =>
                new EmployeeAuthorityVisibilityScope(req.ActorUserId, req.LegalEntityId, false, Array.Empty<Guid>()));

        _employeeRepository
            .Setup(r => r.ListVisibleAsync(
                It.IsAny<Guid>(), It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EmployeeListItemResponse>(), 0));
        _employeeRepository
            .Setup(r => r.ListInvitedPendingByInviterAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EmployeeListItemResponse>());
    }

    private static EmployeeListItemResponse Item(Guid id, string name, Guid? legalEntityId = null) => new(
        id, "E-1", name, $"{name.Replace(" ", ".")}@offboarding.onevo.dev",
        null, null, null, null, legalEntityId, null, "Full-Time", "onboarding", null, null, "pending", DateTimeOffset.UtcNow.AddHours(72));

    private ListEmployeesQueryHandler CreateHandler() =>
        new(_employeeRepository.Object, _authorityResolver.Object, _currentUser.Object);

    private void SetupVisibility(Guid legalEntityId, bool includesSelf, params Guid[] employeeIds) =>
        _authorityResolver
            .Setup(r => r.ResolveVisibilityAsync(
                It.Is<EmployeeAuthorityVisibilityRequest>(req => req.LegalEntityId == legalEntityId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeAuthorityVisibilityScope(_userId, legalEntityId, includesSelf, employeeIds));

    [Fact]
    public async Task Handle_ResolvesLegalEntityFromActorsDefaultEmployee_WhenQueryOmitsLegalEntityId()
    {
        await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        _employeeRepository.Verify(
            r => r.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()), Times.Once);
        _authorityResolver.Verify(
            r => r.ResolveVisibilityAsync(
                It.Is<EmployeeAuthorityVisibilityRequest>(req => req.LegalEntityId == _defaultLegalEntityId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UsesQueryLegalEntityId_WhenGiven_WithoutCallingGetDefaultForUserAsync()
    {
        var explicitLegalEntityId = Guid.NewGuid();

        await CreateHandler().Handle(new ListEmployeesQuery(null, null, explicitLegalEntityId), CancellationToken.None);

        _employeeRepository.Verify(
            r => r.GetDefaultForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _authorityResolver.Verify(
            r => r.ResolveVisibilityAsync(
                It.Is<EmployeeAuthorityVisibilityRequest>(req => req.LegalEntityId == explicitLegalEntityId),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_CallsResolver_WithEmployeeListReadPurposeAndEmployeesReadPermissionAndIncludeSelf()
    {
        await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        _authorityResolver.Verify(r => r.ResolveVisibilityAsync(
            It.Is<EmployeeAuthorityVisibilityRequest>(req =>
                req.ActorUserId == _userId
                && req.RequiredPermission == "employees:read"
                && req.IncludeSelf
                && req.Purpose == EmployeeAuthorityPurpose.EmployeeListRead),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Handle_ReturnsEmptyPage_WithoutCallingListVisibleAsync_WhenResolverReturnsNoVisibleIds()
    {
        SetupVisibility(_defaultLegalEntityId, includesSelf: false);

        var result = await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
        _employeeRepository.Verify(r => r.ListVisibleAsync(
            It.IsAny<Guid>(), It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsEmptyPage_WhenActorHasNoEmployeeRowAnywhere_AndQueryOmitsLegalEntityId()
    {
        _employeeRepository
            .Setup(r => r.GetDefaultForUserAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);

        var result = await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalCount);
        _authorityResolver.Verify(r => r.ResolveVisibilityAsync(
            It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PassesResolvedVisibleIds_IntoRepositoryFilter_RestrictToEmployeeIds()
    {
        var visibleId = Guid.NewGuid();
        SetupVisibility(_defaultLegalEntityId, includesSelf: true, visibleId);

        await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        _employeeRepository.Verify(r => r.ListVisibleAsync(
            _tenantId,
            It.IsAny<EmployeeVisibilityScope>(),
            It.Is<EmployeeListFilter>(f =>
                f.RestrictToEmployeeIds != null
                && f.RestrictToEmployeeIds.Count == 1
                && f.RestrictToEmployeeIds.Contains(visibleId)),
            1, 25, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Handle_NeverPassesUnrestrictedScope_EvenAlongsideRestrictToEmployeeIds()
    {
        // Fail-closed guard: EfEmployeeRepository.ListVisibleAsync only honors RestrictToEmployeeIds
        // when the scope argument is NOT CanViewAllTenantEmployees=true - if a future refactor ever
        // drops the RestrictToEmployeeIds branch, an Unrestricted() scope here would silently widen
        // to every tenant employee instead of failing closed.
        SetupVisibility(_defaultLegalEntityId, includesSelf: true, Guid.NewGuid());

        await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        _employeeRepository.Verify(r => r.ListVisibleAsync(
            It.IsAny<Guid>(),
            It.Is<EmployeeVisibilityScope>(s => !s.CanViewAllTenantEmployees),
            It.IsAny<EmployeeListFilter>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData(0, 25, 1, 25)]
    [InlineData(-5, 25, 1, 25)]
    [InlineData(2, 0, 2, 1)]
    [InlineData(2, 500, 2, 100)]
    public async Task Handle_ClampsPageAndPageSize(int requestedPage, int requestedPageSize, int expectedPage, int expectedPageSize)
    {
        SetupVisibility(_defaultLegalEntityId, includesSelf: true, Guid.NewGuid());

        var result = await CreateHandler().Handle(
            new ListEmployeesQuery(null, null, null, requestedPage, requestedPageSize), CancellationToken.None);

        Assert.Equal(expectedPage, result.Value!.Page);
        Assert.Equal(expectedPageSize, result.Value!.PageSize);
    }

    [Fact]
    public async Task Handle_MergesInPendingInviteesInSameLegalEntity_NotAlreadyInVisiblePage()
    {
        SetupVisibility(_defaultLegalEntityId, includesSelf: true, Guid.NewGuid());
        var visibleEmployee = Item(Guid.NewGuid(), "Ada Lovelace", _defaultLegalEntityId);
        _employeeRepository
            .Setup(r => r.ListVisibleAsync(_tenantId, It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<EmployeeListItemResponse> { visibleEmployee }, 1));
        var pendingInvitee = Item(Guid.NewGuid(), "New Hire", _defaultLegalEntityId);
        _employeeRepository
            .Setup(r => r.ListInvitedPendingByInviterAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeListItemResponse> { pendingInvitee });

        var result = await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Contains(result.Value.Items, i => i.Id == visibleEmployee.Id);
        Assert.Contains(result.Value.Items, i => i.Id == pendingInvitee.Id);
    }

    [Fact]
    public async Task Handle_ExcludesPendingInviteesFromADifferentLegalEntity()
    {
        SetupVisibility(_defaultLegalEntityId, includesSelf: true, Guid.NewGuid());
        var otherLegalEntityId = Guid.NewGuid();
        var pendingInviteeElsewhere = Item(Guid.NewGuid(), "Other Company Hire", otherLegalEntityId);
        _employeeRepository
            .Setup(r => r.ListInvitedPendingByInviterAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeListItemResponse> { pendingInviteeElsewhere });

        var result = await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.DoesNotContain(result.Value!.Items, i => i.Id == pendingInviteeElsewhere.Id);
    }

    [Fact]
    public async Task Handle_DoesNotDuplicate_WhenPendingInviteeIsAlreadyInVisiblePage()
    {
        SetupVisibility(_defaultLegalEntityId, includesSelf: true, Guid.NewGuid());
        var alreadyVisibleInvitee = Item(Guid.NewGuid(), "Both Ways", _defaultLegalEntityId);
        _employeeRepository
            .Setup(r => r.ListVisibleAsync(_tenantId, It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<EmployeeListItemResponse> { alreadyVisibleInvitee }, 1));
        _employeeRepository
            .Setup(r => r.ListInvitedPendingByInviterAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeListItemResponse> { alreadyVisibleInvitee });

        var result = await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Single(result.Value.Items);
    }

    [Fact]
    public async Task Handle_AlwaysCallsListInvitedPendingByInviterAsync_EvenWhenResolverReturnsNoVisibleIds()
    {
        SetupVisibility(_defaultLegalEntityId, includesSelf: false);

        await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        _employeeRepository.Verify(
            r => r.ListInvitedPendingByInviterAsync(_tenantId, _userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
