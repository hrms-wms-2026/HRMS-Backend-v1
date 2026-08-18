using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class ListEmployeesQueryHandlerTests
{
    private readonly Mock<IEmployeeRepository> _employeeRepository = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public ListEmployeesQueryHandlerTests()
    {
        _currentUser.SetupGet(u => u.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
        _employeeRepository
            .Setup(r => r.ListVisibleAsync(
                It.IsAny<Guid>(), It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<EmployeeListItemResponse>(), 0));
        _employeeRepository
            .Setup(r => r.ListInvitedPendingByInviterAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<EmployeeListItemResponse>());
    }

    private static EmployeeListItemResponse Item(Guid id, string name) => new(
        id, "E-1", name, $"{name.Replace(" ", ".")}@offboarding.onevo.dev",
        null, null, null, null, null, null, "Full-Time", "onboarding", null, null, "pending", DateTimeOffset.UtcNow.AddHours(72));

    private ListEmployeesQueryHandler CreateHandler() =>
        new(_employeeRepository.Object, _scopeResolver.Object, _currentUser.Object);

    [Fact]
    public async Task Handle_UsesUnrestrictedScope_AndSkipsResolver_WhenCallerHasOrgManage()
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);

        var result = await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _scopeResolver.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _employeeRepository.Verify(r => r.ListVisibleAsync(
            _tenantId,
            It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees),
            It.IsAny<EmployeeListFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Handle_CallsResolver_AndForwardsItsScope_WhenCallerLacksOrgManage()
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        var resolvedScope = new EmployeeVisibilityScope(
            false, Guid.NewGuid(), new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>());
        _scopeResolver
            .Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedScope);

        await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        _employeeRepository.Verify(r => r.ListVisibleAsync(
            _tenantId, resolvedScope, It.IsAny<EmployeeListFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()));
    }

    [Theory]
    [InlineData(0, 25, 1, 25)]
    [InlineData(-5, 25, 1, 25)]
    [InlineData(2, 0, 2, 1)]
    [InlineData(2, 500, 2, 100)]
    public async Task Handle_ClampsPageAndPageSize(int requestedPage, int requestedPageSize, int expectedPage, int expectedPageSize)
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);

        var result = await CreateHandler().Handle(
            new ListEmployeesQuery(null, null, null, requestedPage, requestedPageSize), CancellationToken.None);

        Assert.Equal(expectedPage, result.Value!.Page);
        Assert.Equal(expectedPageSize, result.Value!.PageSize);
    }

    [Fact]
    public async Task Handle_NoOrgManage_MergesInPendingInviteesNotAlreadyInCoverageScopedPage()
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        _scopeResolver
            .Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        var coveredEmployee = Item(Guid.NewGuid(), "Ada Lovelace");
        _employeeRepository
            .Setup(r => r.ListVisibleAsync(_tenantId, It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<EmployeeListItemResponse> { coveredEmployee }, 1));
        var pendingInvitee = Item(Guid.NewGuid(), "New Hire");
        _employeeRepository
            .Setup(r => r.ListInvitedPendingByInviterAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeListItemResponse> { pendingInvitee });

        var result = await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.Equal(2, result.Value!.TotalCount);
        Assert.Contains(result.Value.Items, i => i.Id == coveredEmployee.Id);
        Assert.Contains(result.Value.Items, i => i.Id == pendingInvitee.Id);
    }

    [Fact]
    public async Task Handle_NoOrgManage_DoesNotDuplicate_WhenPendingInviteeIsAlreadyInCoverageScopedPage()
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        _scopeResolver
            .Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()));
        var alreadyCoveredInvitee = Item(Guid.NewGuid(), "Both Ways");
        _employeeRepository
            .Setup(r => r.ListVisibleAsync(_tenantId, It.IsAny<EmployeeVisibilityScope>(), It.IsAny<EmployeeListFilter>(), 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<EmployeeListItemResponse> { alreadyCoveredInvitee }, 1));
        _employeeRepository
            .Setup(r => r.ListInvitedPendingByInviterAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EmployeeListItemResponse> { alreadyCoveredInvitee });

        var result = await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Single(result.Value.Items);
    }

    [Fact]
    public async Task Handle_OrgManage_NeverCallsListInvitedPendingByInviterAsync()
    {
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);

        await CreateHandler().Handle(new ListEmployeesQuery(null, null, null), CancellationToken.None);

        _employeeRepository.Verify(
            r => r.ListInvitedPendingByInviterAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
