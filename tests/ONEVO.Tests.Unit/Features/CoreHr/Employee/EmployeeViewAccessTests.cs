using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Access;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using Xunit;
using CommonEmployeeRepo = ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository;
using VisibleEmployeeRepo = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class EmployeeViewAccessTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<CommonEmployeeRepo> _employees = new();
    private readonly Mock<VisibleEmployeeRepo> _visibleEmployees = new();
    private readonly Mock<IEmployeeVisibilityScopeResolver> _scopeResolver = new();

    public EmployeeViewAccessTests()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(c => c.TenantId).Returns(_tenantId);
        _currentUser.SetupGet(c => c.UserId).Returns(_userId);
        // No permission codes at all: visibility must come from the relationship alone.
        _currentUser.Setup(c => c.HasPermission(It.IsAny<string>())).Returns(false);
        _employees.Setup(r => r.GetByUserIdAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeEntity { Id = _employeeId, TenantId = _tenantId });
        _scopeResolver.Setup(r => r.ResolveAsync(_tenantId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeVisibilityScope(
                false, _employeeId, new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid>(), new HashSet<Guid>()));
    }

    private EmployeeViewAccess Sut() =>
        new(_employees.Object, _visibleEmployees.Object, _scopeResolver.Object, _currentUser.Object);

    private void MakeVisible(Guid employeeId) =>
        _visibleEmployees.Setup(r => r.GetVisibleByIdAsync(_tenantId, It.IsAny<EmployeeVisibilityScope>(), employeeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmployeeListItemResponse(employeeId, "E-1", "Ada", "ada@test.dev", null, null, null, null, null, null, "full_time", "active", null, null));

    [Fact]
    public async Task Self_IsAlwaysVisible()
    {
        Assert.True(await Sut().CanViewAsync(_employeeId));
    }

    [Fact]
    public async Task EmployeeInsideCoverage_IsVisible_WithoutAnyPermission()
    {
        var report = Guid.NewGuid();
        MakeVisible(report);

        Assert.True(await Sut().CanViewAsync(report));
    }

    [Fact]
    public async Task EmployeeOutsideCoverage_IsNotVisible()
    {
        Assert.False(await Sut().CanViewAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Unauthenticated_IsNotVisible()
    {
        _currentUser.SetupGet(c => c.IsAuthenticated).Returns(false);

        Assert.False(await Sut().CanViewAsync(_employeeId));
    }
}
