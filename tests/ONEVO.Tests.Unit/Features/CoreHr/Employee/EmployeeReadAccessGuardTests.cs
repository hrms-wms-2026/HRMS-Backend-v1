using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Infrastructure.Services.CoreHr;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public sealed class EmployeeReadAccessGuardTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public EmployeeReadAccessGuardTests()
    {
        _currentUser.SetupGet(u => u.UserId).Returns(_userId);
    }

    private EmployeeReadAccessGuard CreateGuard() =>
        new(_currentUser.Object, _scopeResolver.Object, _employees.Object);

    private static EmployeeListItemResponse VisibleRow(Guid id) =>
        new(id, "E-001", "Ada Lovelace", "ada@test.dev",
            null, null, null, "Engineer", null, null, "full_time", "active", null, null);

    private void ArrangeExisting() =>
        _employees.Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });

    [Fact]
    public async Task EnsureCanRead_ReturnsNotFound_WhenEmployeeDoesNotExistInTenant()
    {
        _employees.Setup(r => r.GetByIdAsync(_tenantId, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeEntity?)null);

        var result = await CreateGuard().EnsureCanRead(_tenantId, _employeeId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task EnsureCanRead_ReturnsForbidden_WhenEmployeeIsOutsideCallerScope()
    {
        ArrangeExisting();
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        var scope = new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        _employees.Setup(r => r.GetVisibleByIdAsync(_tenantId, scope, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeListItemResponse?)null);

        var result = await CreateGuard().EnsureCanRead(_tenantId, _employeeId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task EnsureCanRead_ReturnsVisibleRow_WhenEmployeeIsInsideCallerScope()
    {
        ArrangeExisting();
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(false);
        var scope = new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        _employees.Setup(r => r.GetVisibleByIdAsync(_tenantId, scope, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisibleRow(_employeeId));

        var result = await CreateGuard().EnsureCanRead(_tenantId, _employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Ada Lovelace", result.Value!.FullName);
    }

    [Fact]
    public async Task EnsureCanRead_SkipsScopeResolution_WhenCallerHasOrgManage()
    {
        ArrangeExisting();
        _currentUser.Setup(u => u.HasPermission("org:manage")).Returns(true);
        _employees.Setup(r => r.GetVisibleByIdAsync(
                _tenantId, It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(VisibleRow(_employeeId));

        var result = await CreateGuard().EnsureCanRead(_tenantId, _employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _scopeResolver.Verify(
            r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
