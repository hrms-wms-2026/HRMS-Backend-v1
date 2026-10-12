using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Queries;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Dashboard.Team;

public sealed class GetMyTeamCapabilitiesQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ActorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid LegalEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task Forbidden_when_not_authenticated()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(false);
        var handler = CreateHandler(currentUser, out _, out _, out _);

        var result = await handler.Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task No_employee_record_gives_false_people_and_work_flags_but_permission_flags_still_compute()
    {
        var currentUser = CurrentUser(permissions: ["leave:approve"]);
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);
        var handler = CreateHandler(currentUser, out _, out _, out _, employees: employees);

        var result = await handler.Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);

        var response = result.Value!;
        Assert.False(response.CanViewPeopleStatus);
        Assert.False(response.LeadsWork);
        Assert.False(response.HasWorkApprovals);
        Assert.Null(response.LegalEntityId);
        Assert.True(response.CanReviewPeopleApprovals);
        Assert.True(response.IsAvailable);
    }

    [Theory]
    [InlineData("leave:approve")]
    [InlineData("attendance:approve")]
    public async Task CanReviewPeopleApprovals_true_via_either_permission(string permission)
    {
        var currentUser = CurrentUser(permissions: [permission]);
        var handler = CreateHandler(currentUser, out _, out _, out _);

        var result = await handler.Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);

        Assert.True(result.Value!.CanReviewPeopleApprovals);
    }

    [Theory]
    [InlineData("employees:write")]
    [InlineData("exceptions:manage")]
    [InlineData("attendance:approve")]
    [InlineData("exceptions:view")]
    public async Task CanReviewExceptions_true_via_any_of_the_four_permissions(string permission)
    {
        var currentUser = CurrentUser(permissions: [permission]);
        var handler = CreateHandler(currentUser, out _, out _, out _);

        var result = await handler.Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);

        Assert.True(result.Value!.CanReviewExceptions);
    }

    [Fact]
    public async Task CanViewPeopleStatus_requires_both_permission_and_HasAnyManagedCoverageAsync()
    {
        var currentUser = CurrentUser(permissions: ["attendance:read"]);
        var handler = CreateHandler(currentUser, out var authority, out _, out _);
        authority.Setup(x => x.HasAnyManagedCoverageAsync(
                It.Is<EmployeeAuthorityVisibilityRequest>(r => r.ActorUserId == UserId && r.LegalEntityId == LegalEntityId && r.RequiredPermission == "attendance:read"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await handler.Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);

        Assert.True(result.Value!.CanViewPeopleStatus);
    }

    [Fact]
    public async Task CanViewPeopleStatus_false_and_resolver_never_called_when_permission_is_missing()
    {
        var currentUser = CurrentUser(permissions: []);
        var handler = CreateHandler(currentUser, out var authority, out _, out _);

        var result = await handler.Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);

        Assert.False(result.Value!.CanViewPeopleStatus);
        authority.Verify(x => x.HasAnyManagedCoverageAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LeadsWork_and_HasWorkApprovals_false_and_never_queried_when_no_work_module_active()
    {
        var currentUser = CurrentUser(permissions: []);
        var handler = CreateHandler(currentUser, out _, out var modules, out var workLeadership, activeModules: ["payroll"]);

        var result = await handler.Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);

        Assert.False(result.Value!.LeadsWork);
        Assert.False(result.Value.HasWorkApprovals);
        workLeadership.Verify(x => x.LeadsAnyWorkAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        workLeadership.Verify(x => x.HasPendingWorkApprovalsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LeadsWork_and_HasWorkApprovals_true_when_module_active_and_service_says_so()
    {
        var currentUser = CurrentUser(permissions: []);
        var handler = CreateHandler(currentUser, out _, out _, out var workLeadership, activeModules: ["tasks"]);
        workLeadership.Setup(x => x.LeadsAnyWorkAsync(TenantId, ActorId, LegalEntityId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        workLeadership.Setup(x => x.HasPendingWorkApprovalsAsync(TenantId, ActorId, LegalEntityId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await handler.Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);

        Assert.True(result.Value!.LeadsWork);
        Assert.True(result.Value.HasWorkApprovals);
    }

    [Fact]
    public async Task IsAvailable_is_false_when_every_flag_is_false()
    {
        var currentUser = CurrentUser(permissions: []);
        var handler = CreateHandler(currentUser, out _, out _, out _, activeModules: []);

        var result = await handler.Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);

        Assert.False(result.Value!.IsAvailable);
        Assert.False(result.Value.CanViewLiveActivity);
    }

    private static GetMyTeamCapabilitiesQueryHandler CreateHandler(
        Mock<ICurrentUser> currentUser,
        out Mock<IEmployeeAuthorityResolver> authority,
        out Mock<IModuleEntitlementService> modules,
        out Mock<IWorkLeadershipService> workLeadership,
        Mock<IEmployeeRepository>? employees = null,
        string[]? activeModules = null)
    {
        employees ??= EmployeesWithActor();
        authority = new Mock<IEmployeeAuthorityResolver>();
        authority.Setup(x => x.HasAnyManagedCoverageAsync(It.IsAny<EmployeeAuthorityVisibilityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        modules = new Mock<IModuleEntitlementService>();
        modules.Setup(x => x.GetActiveModuleKeysForTenantAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeModules ?? Array.Empty<string>());
        workLeadership = new Mock<IWorkLeadershipService>();
        workLeadership.Setup(x => x.LeadsAnyWorkAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        workLeadership.Setup(x => x.HasPendingWorkApprovalsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        return new GetMyTeamCapabilitiesQueryHandler(
            currentUser.Object, employees.Object, authority.Object, modules.Object, workLeadership.Object);
    }

    private static Mock<ICurrentUser> CurrentUser(IReadOnlyList<string> permissions)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        currentUser.SetupGet(x => x.UserId).Returns(UserId);
        foreach (var permission in permissions)
            currentUser.Setup(x => x.HasPermission(permission)).Returns(true);
        return currentUser;
    }

    private static Mock<IEmployeeRepository> EmployeesWithActor()
    {
        var employees = new Mock<IEmployeeRepository>();
        employees.Setup(x => x.GetDefaultForUserAsync(TenantId, UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = ActorId, UserId = UserId, TenantId = TenantId, LegalEntityId = LegalEntityId });
        return employees;
    }
}
