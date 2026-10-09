using FluentAssertions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Tests.Integration.Support;
using Xunit;
using ManagementCoverageRecord = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>Proves the resolver batching (Part 1 Tasks 2-3) and request-scoped memo (Part 1 Task 4)
/// hold against real PostgreSQL: a constant round-trip count regardless of how much coverage is
/// configured, and a second call reusing the same (actor, legal entity) touches far fewer
/// commands. Requires Docker (Testcontainers PostgreSQL).</summary>
public sealed class EmployeeAuthorityResolverBudgetIntegrationTests
{
    private sealed class StubCurrentUser(Guid tenantId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => string.Empty;
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    private static EmployeeAuthorityResolver Build(ApplicationDbContext db, Guid tenantId, Guid userId) => new(
        new StubCurrentUser(tenantId, userId),
        new SystemDateTimeProvider(),
        new EfEmployeeRepository(db),
        PositionAssignmentRepositoryTestSupport.CreateRepository(db),
        new EfPositionRepository(db),
        PositionAssignmentRepositoryTestSupport.CreateClosureRepository(db),
        new EfDepartmentRepository(db),
        new EfPermissionRepository(db));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(6, 4)]
    public async Task First_call_is_constant_and_second_call_reuses_the_expansion(int coveredPositions, int coveredDepartments)
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid actorUserId;
        await using (var db = helper.NewContext())
        {
            var actor = helper.AddEmployee(db);
            actorUserId = actor.UserId;
            var actorPosition = helper.AddPositionHeldBy(db, actor.Id);
            for (var i = 0; i < coveredPositions; i++)
            {
                var member = helper.AddEmployee(db);
                helper.AddCoverage(db, actorPosition, ManagementCoverageRecord.TargetPosition,
                    coveredPositionId: helper.AddPositionHeldBy(db, member.Id));
            }
            for (var i = 0; i < coveredDepartments; i++)
            {
                var root = helper.AddDepartment(db);
                helper.AddEmployee(db, departmentId: helper.AddDepartment(db, root));
                helper.AddCoverage(db, actorPosition, ManagementCoverageRecord.TargetDepartment, coveredDepartmentId: root);
            }
            helper.GrantPermission(db, actorUserId, "attendance:read");
            helper.GrantPermission(db, actorUserId, "attendance:approve");
            await db.SaveChangesAsync();
        }

        var counter = new CountingDbCommandInterceptor();
        await using var read = helper.NewContext(counter);
        var resolver = Build(read, helper.TenantId, actorUserId);

        var first = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actorUserId, helper.LegalEntityId, "attendance:read", false, EmployeeAuthorityPurpose.TimeTrackingRead));
        var firstCommands = counter.Count;
        counter.Reset();
        var second = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actorUserId, helper.LegalEntityId, "attendance:approve", false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        first.EmployeeIds.Should().HaveCount(coveredPositions + coveredDepartments);
        second.EmployeeIds.Should().BeEquivalentTo(first.EmployeeIds);
        // First call with both position and department coverage is 9 round trips, independent of
        // how many positions/departments are covered: actor lookup, permission check, actor's
        // primary assignment, coverage rows, covered-position holders (batched), closure
        // descendants, descendant departments (one CTE), active employees in those departments,
        // final active-id filter. (7 is the count when only one coverage branch runs.)
        firstCommands.Should().BeLessThanOrEqualTo(9);
        counter.Count.Should().BeLessThanOrEqualTo(2);
    }
}
