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
    }

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
}
