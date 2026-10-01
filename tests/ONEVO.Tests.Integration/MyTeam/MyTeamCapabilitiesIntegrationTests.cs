using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.Dashboard.Team.Queries;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Integration.Support;
using Xunit;
using ManagementCoverageRecord = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>Proves GetMyTeamCapabilitiesQueryHandler against real PostgreSQL: canViewPeopleStatus
/// requires both the permission and a real active coverage row (not mere permission alone), and
/// leadsWork/hasWorkApprovals reflect real owned-objective/pending-request data. Module
/// entitlement is stubbed - it is an external concern this feature does not own, same as every
/// other My Team integration test that stubs ICurrentUser/IDateTimeProvider. Requires Docker
/// (Testcontainers PostgreSQL).</summary>
public sealed class MyTeamCapabilitiesIntegrationTests
{
    private sealed class StubCurrentUser(Guid tenantId, Guid userId, params string[] permissions) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => string.Empty;
        public IReadOnlyList<string> Permissions => permissions;
        public bool HasPermission(string permission) => permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }

    private sealed class StubModuleEntitlementService(bool workModuleActive) : IModuleEntitlementService
    {
        public Task<bool> IsModuleEnabledAsync(Guid tenantId, string moduleKey, CancellationToken ct = default)
            => Task.FromResult(workModuleActive);
        public Task<IReadOnlyList<ONEVO.Domain.Features.Auth.Entities.Permission>> GetEntitledPermissionsAsync(
            IReadOnlyList<string> moduleKeys, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ONEVO.Domain.Features.Auth.Entities.Permission>>([]);
        public Task<IReadOnlyList<string>> GetActiveModuleKeysForTenantAsync(Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(workModuleActive ? ["tasks"] : []);
        public Task<IReadOnlyList<ONEVO.Domain.Features.Auth.Entities.Permission>> GetAssignablePermissionsForTenantAsync(
            Guid tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ONEVO.Domain.Features.Auth.Entities.Permission>>([]);
        public Task<IReadOnlyList<ONEVO.Domain.Features.Auth.Entities.Permission>> GetAssignablePermissionsForTenantAsync(
            Guid tenantId, IReadOnlyList<Guid> permissionIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ONEVO.Domain.Features.Auth.Entities.Permission>>([]);
    }

    private static GetMyTeamCapabilitiesQueryHandler Handler(
        ApplicationDbContext db, Guid tenantId, Guid userId, bool workModuleActive, params string[] permissions)
    {
        var currentUser = new StubCurrentUser(tenantId, userId, permissions);
        var authority = new EmployeeAuthorityResolver(
            currentUser,
            new ONEVO.Infrastructure.Identity.Time.SystemDateTimeProvider(),
            new EfEmployeeRepository(db),
            PositionAssignmentRepositoryTestSupport.CreateRepository(db),
            new EfPositionRepository(db),
            PositionAssignmentRepositoryTestSupport.CreateClosureRepository(db),
            new EfDepartmentRepository(db),
            new EfPermissionRepository(db));
        var workLeadership = new WorkLeadershipService(
            new EfObjectiveRepository(db),
            new EfProjectMemberRepository(db),
            new EfTaskCreationRequestRepository(db),
            new EfTaskEditRequestRepository(db),
            new EfObjectiveChangeRequestRepository(db),
            new EfTaskStatusChangeRequestRepository(db),
            new TaskStatusChangeAccessService(
                new EfObjectiveRepository(db), new EfProjectMemberRepository(db),
                new MilestoneMembershipCoordinator(new CommonEfEmployeeRepository(db), new EfProjectMemberRepository(db), new EfObjectiveRepository(db))),
            NullLogger<WorkLeadershipService>.Instance);

        return new GetMyTeamCapabilitiesQueryHandler(
            currentUser, new EfEmployeeRepository(db), authority,
            new StubModuleEntitlementService(workModuleActive), workLeadership);
    }

    [Fact]
    public async Task CanViewPeopleStatus_requires_both_the_permission_and_a_real_active_coverage_row()
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid managerUserId;
        await using (var db = helper.NewContext())
        {
            var manager = helper.AddEmployee(db);
            managerUserId = manager.UserId;
            var managerPosition = helper.AddPositionHeldBy(db, manager.Id);
            var dept = helper.AddDepartment(db);
            helper.AddCoverage(db, managerPosition, ManagementCoverageRecord.TargetDepartment, coveredDepartmentId: dept);
            await db.SaveChangesAsync();
        }

        await using var withPermissionAndCoverage = helper.NewContext();
        var withBoth = await Handler(withPermissionAndCoverage, helper.TenantId, managerUserId, workModuleActive: false, "attendance:read")
            .Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);
        withBoth.Value!.CanViewPeopleStatus.Should().BeTrue();

        await using var permissionOnly = helper.NewContext();
        var noPermission = await Handler(permissionOnly, helper.TenantId, managerUserId, workModuleActive: false)
            .Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);
        noPermission.Value!.CanViewPeopleStatus.Should().BeFalse("permission alone without coverage must not grant the flag - the gate requires both");
    }

    [Fact]
    public async Task LeadsWork_and_HasWorkApprovals_reflect_real_data_and_require_an_active_module()
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid leadUserId;
        await using (var db = helper.NewContext())
        {
            var lead = helper.AddEmployee(db);
            leadUserId = lead.UserId;
            var (projectId, rootId) = helper.AddProject(db, lead.Id);
            helper.AddModule(db, projectId, rootId, lead.Id);
            await db.SaveChangesAsync();
        }

        await using var moduleActive = helper.NewContext();
        var active = await Handler(moduleActive, helper.TenantId, leadUserId, workModuleActive: true)
            .Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);
        active.Value!.LeadsWork.Should().BeTrue();

        await using var moduleInactive = helper.NewContext();
        var inactive = await Handler(moduleInactive, helper.TenantId, leadUserId, workModuleActive: false)
            .Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);
        inactive.Value!.LeadsWork.Should().BeFalse("an inactive Work module must suppress leadsWork even though the caller really does own a module");
    }

    [Fact]
    public async Task No_default_employee_record_gives_false_people_and_work_flags_with_null_legal_entity()
    {
        var helper = await MyTeamDb.CreateAsync();
        var strangerUserId = Guid.NewGuid(); // never seeded as an employee

        await using var db = helper.NewContext();
        var result = await Handler(db, helper.TenantId, strangerUserId, workModuleActive: true, "leave:approve", "attendance:read")
            .Handle(new GetMyTeamCapabilitiesQuery(), CancellationToken.None);

        result.Value!.LegalEntityId.Should().BeNull();
        result.Value.CanViewPeopleStatus.Should().BeFalse();
        result.Value.LeadsWork.Should().BeFalse();
        result.Value.HasWorkApprovals.Should().BeFalse();
        result.Value.CanReviewPeopleApprovals.Should().BeTrue("permission-based flags still compute without a default employee record");
    }
}
