using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Calendar.Services;
using Xunit;
using DomainEmployee = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public sealed class LeaveVisibilityScopeProviderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static (LeaveVisibilityScopeProvider Sut, Mock<IEmployeeVisibilityScopeResolver> Scopes, Mock<IEmployeeRepository> Employees)
        Build(params string[] permissions)
    {
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.TenantId).Returns(TenantId);
        user.SetupGet(x => x.UserId).Returns(UserId);
        user.Setup(x => x.HasPermission(It.IsAny<string>())).Returns<string>(p => permissions.Contains(p));
        var scopes = new Mock<IEmployeeVisibilityScopeResolver>();
        var employees = new Mock<IEmployeeRepository>();
        return (new LeaveVisibilityScopeProvider(user.Object, employees.Object, scopes.Object), scopes, employees);
    }

    [Theory]
    [InlineData("leave:read")]
    [InlineData("leave:manage")]
    public async Task Read_or_manage_is_unrestricted(string permission)
    {
        var (sut, _, _) = Build(permission);
        var result = await sut.ResolveForCurrentUserAsync();
        Assert.True(result.Scope!.CanViewAllTenantEmployees);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task Read_team_uses_the_raw_coverage_scope_resolver()
    {
        var (sut, scopes, _) = Build("leave:read-team");
        var expected = new EmployeeVisibilityScope(false, Guid.NewGuid(), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>());
        scopes.Setup(x => x.ResolveAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await sut.ResolveForCurrentUserAsync();

        Assert.Same(expected, result.Scope);
    }

    [Fact]
    public async Task Read_own_is_self_only()
    {
        var (sut, _, employees) = Build("leave:read-own");
        var me = new DomainEmployee { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId };
        employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(me);

        var result = await sut.ResolveForCurrentUserAsync();

        Assert.False(result.Scope!.CanViewAllTenantEmployees);
        Assert.Equal(me.Id, result.Scope.OwnEmployeeId);
    }

    [Fact]
    public async Task Read_own_without_employee_fails_with_NoEmployee()
    {
        var (sut, _, employees) = Build("leave:read-own");
        employees.Setup(x => x.GetByUserIdAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainEmployee?)null);

        var result = await sut.ResolveForCurrentUserAsync();

        Assert.Equal(LeaveVisibilityScopeFailure.NoEmployee, result.Failure);
    }

    [Fact]
    public async Task No_leave_permission_fails_with_NoLeaveReadPermission()
    {
        var (sut, _, _) = Build("attendance:read");
        var result = await sut.ResolveForCurrentUserAsync();
        Assert.Equal(LeaveVisibilityScopeFailure.NoLeaveReadPermission, result.Failure);
        Assert.Null(result.Scope);
    }
}
