using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Infrastructure.Services.CoreHr;
using IEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class EmployeeManageScopeGuardTests
{
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _resolver = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();

    public EmployeeManageScopeGuardTests()
    {
        _currentUser.Setup(c => c.UserId).Returns(_userId);
    }

    private EmployeeManageScopeGuard CreateSut() => new(_currentUser.Object, _resolver.Object, _employees.Object);

    private static EmployeeListItemResponse Row(Guid id) =>
        new(id, "E-001", "Ada Lovelace", "ada@example.com", null, null, null, null, null, null, "Full-time", "active", null, null);

    [Fact]
    public async Task OrgManage_UsesUnrestrictedScope_WithoutResolving()
    {
        _currentUser.Setup(c => c.HasPermission("org:manage")).Returns(true);
        _employees.Setup(e => e.GetVisibleByIdAsync(_tenantId, It.Is<EmployeeVisibilityScope>(s => s.CanViewAllTenantEmployees), _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Row(_employeeId));

        var result = await CreateSut().EnsureCanManage(_tenantId, _employeeId);

        Assert.Null(result);
        _resolver.Verify(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InScope_ReturnsNull_UsingResolvedScope()
    {
        var scope = new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>());
        _resolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        _employees.Setup(e => e.GetVisibleByIdAsync(_tenantId, scope, _employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(Row(_employeeId));

        var result = await CreateSut().EnsureCanManage(_tenantId, _employeeId);

        Assert.Null(result);
    }

    [Fact]
    public async Task OutOfScope_ReturnsForbidden()
    {
        var scope = new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        _resolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(scope);
        _employees.Setup(e => e.GetVisibleByIdAsync(_tenantId, scope, _employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EmployeeListItemResponse?)null);

        var result = await CreateSut().EnsureCanManage(_tenantId, _employeeId);

        Assert.NotNull(result);
        Assert.Equal(403, result!.StatusCode);
        Assert.Equal(EmployeeManageScopeGuard.ForbiddenMessage, result.Error);
    }
}
