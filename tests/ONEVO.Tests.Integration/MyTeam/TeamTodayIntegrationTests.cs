using FluentAssertions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Services;
using ONEVO.Application.Features.Leave.Calendar.Services;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Auth.Login;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Request;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Type;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Infrastructure.Persistence.Repositories.TimeAttendance;
using ONEVO.Tests.Integration.Support;
using Xunit;
using ManagementCoverageRecord = ONEVO.Domain.Features.OrgStructure.Entities.ManagementCoverageRecord;
// Distinct from ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository (used
// everywhere else below) - this one implements the separate Common.RepositoryInterfaces
// IEmployeeRepository that LeaveVisibilityScopeProvider itself depends on.
using CommonEfEmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>Proves Team Status (My Team spec §8.1) against real PostgreSQL: population scoping
/// via IEmployeeAuthorityResolver (People I Manage coverage, not raw department membership),
/// status mapping through the same AttendanceDayStatusResolver + TeamStatusMapper used
/// everywhere else, leave masking (§8.1.2 rule 5 / D6) toggling with the caller's own leave
/// permission, and a constant command-count budget regardless of team size. Requires Docker
/// (Testcontainers PostgreSQL).</summary>
public sealed class TeamTodayIntegrationTests
{
    // Noon UTC on a day the seeded legal entity (Timezone "UTC", WorkStartTime 09:00) always
    // treats as a working day - deterministic regardless of when the suite actually runs, and
    // safely after the scheduled start so an unrecorded employee resolves to "absent", not
    // "not_started".
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.Parse("2026-09-30T12:00:00+00:00");
    private static readonly DateOnly WorkDate = DateOnly.FromDateTime(FixedNow.UtcDateTime);

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => FixedNow;
        public DateOnly Today => WorkDate;
    }

    private sealed class StubCurrentUser(Guid tenantId, Guid userId, params string[] permissions) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => string.Empty;
        public IReadOnlyList<string> Permissions => permissions;
        public bool HasPermission(string permission) => permissions.Contains(permission);
        public bool IsAuthenticated => true;
    }

    private sealed class NotUsedTodayStateService : IAttendanceTodayStateService
    {
        public Task<Result<AttendanceTodayResponse>> GetTodayAsync(CancellationToken ct = default)
            => throw new NotImplementedException("Team Status never calls the personal today-state service.");
        public Task<Result<AttendanceTodayResponse>> GetTodayAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => throw new NotImplementedException("Team Status never calls the personal today-state service.");
        public Task<Result<AttendanceTodayContext>> ResolveContextAsync(CancellationToken ct = default)
            => throw new NotImplementedException("Team Status never calls the personal today-state service.");
        public Task<Result<AttendanceTodayContext>> ResolveContextAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
            => throw new NotImplementedException("Team Status never calls the personal today-state service.");
    }

    private static AttendanceReadHandler Handler(ApplicationDbContext db, Guid tenantId, Guid userId, params string[] permissions)
    {
        var currentUser = new StubCurrentUser(tenantId, userId, permissions);
        var employees = new EfEmployeeRepository(db);
        var authority = new EmployeeAuthorityResolver(
            currentUser,
            new FixedClock(),
            employees,
            PositionAssignmentRepositoryTestSupport.CreateRepository(db),
            new EfPositionRepository(db),
            PositionAssignmentRepositoryTestSupport.CreateClosureRepository(db),
            new EfDepartmentRepository(db),
            new EfPermissionRepository(db));
        var leaveVisibilityScope = new LeaveVisibilityScopeProvider(
            currentUser, new CommonEfEmployeeRepository(db), new EmployeeVisibilityScopeResolver(db));

        return new AttendanceReadHandler(
            currentUser,
            employees,
            new EfAttendanceReadRepository(db),
            authority,
            new NotUsedTodayStateService(),
            leaveRequests: new EfLeaveRequestReadRepository(db),
            legalEntities: new EfLegalEntityRepository(db),
            dateTimeProvider: new FixedClock(),
            policies: new EfClockInPolicyRepository(db),
            positionAssignments: PositionAssignmentRepositoryTestSupport.CreateRepository(db),
            leaveTypes: new EfLeaveTypeRepository(db),
            leaveVisibilityScope: leaveVisibilityScope);
    }

    private static async Task<(MyTeamDb Db, Guid ManagerUserId, Guid WorkingId, Guid LateId, Guid AbsentId, Guid OnLeaveId, Guid LeaveTypeId)> SeedAsync()
    {
        var helper = await MyTeamDb.CreateAsync();
        await using var db = helper.NewContext();

        var manager = helper.AddEmployee(db);
        var managerPosition = helper.AddPositionHeldBy(db, manager.Id);
        var coveredDept = helper.AddDepartment(db);
        var strangerDept = helper.AddDepartment(db);

        var working = helper.AddEmployee(db, departmentId: coveredDept, lastName: "Working");
        var late = helper.AddEmployee(db, departmentId: coveredDept, lastName: "Late");
        var absent = helper.AddEmployee(db, departmentId: coveredDept, lastName: "Absent");
        var onLeave = helper.AddEmployee(db, departmentId: coveredDept, lastName: "OnLeave");
        helper.AddEmployee(db, departmentId: strangerDept, lastName: "Stranger"); // never covered

        helper.AddCoverage(db, managerPosition, ManagementCoverageRecord.TargetDepartment, coveredDepartmentId: coveredDept);
        helper.AddClockInPolicy(db);

        helper.AddAttendanceRecord(db, working.Id, WorkDate,
            actualStart: FixedNow.AddHours(-4), status: AttendanceRecord.StatusOnTime);
        helper.AddAttendanceRecord(db, late.Id, WorkDate,
            actualStart: FixedNow.AddHours(-4).AddMinutes(15), status: AttendanceRecord.StatusLate);
        // absent: deliberately no attendance record at all.

        var leaveTypeId = helper.AddLeaveType(db);
        helper.AddApprovedLeave(db, onLeave.Id, leaveTypeId, WorkDate, WorkDate);

        helper.GrantPermission(db, manager.UserId, "attendance:read");

        await db.SaveChangesAsync();
        return (helper, manager.UserId, working.Id, late.Id, absent.Id, onLeave.Id, leaveTypeId);
    }

    [Fact]
    public async Task Manager_sees_only_covered_employees_with_correct_statuses()
    {
        var seeded = await SeedAsync();
        await using var db = seeded.Db.NewContext();

        var result = await Handler(db, seeded.Db.TenantId, seeded.ManagerUserId, "attendance:read")
            .Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value!;
        response.Members.Should().HaveCount(4, "the stranger outside coverage must never appear");
        response.Members.Should().OnlyContain(m => m.DisplayName != null && !m.DisplayName.Contains("Stranger"));

        response.Members.Single(m => m.EmployeeId == seeded.WorkingId).Status.Should().Be("working");
        response.Members.Single(m => m.EmployeeId == seeded.WorkingId).IsLate.Should().BeFalse();

        var lateMember = response.Members.Single(m => m.EmployeeId == seeded.LateId);
        lateMember.Status.Should().Be("working");
        lateMember.IsLate.Should().BeTrue();

        var absentMember = response.Members.Single(m => m.EmployeeId == seeded.AbsentId);
        absentMember.Status.Should().Be("absent");
        absentMember.AttentionSeverity.Should().Be("critical");

        response.Summary.Total.Should().Be(4);
        response.Summary.Working.Should().Be(2);
        response.Summary.Late.Should().Be(1);
    }

    [Fact]
    public async Task Leave_masking_toggles_with_the_callers_own_leave_permission()
    {
        var seeded = await SeedAsync();

        await using var unauthorizedDb = seeded.Db.NewContext();
        var unauthorized = await Handler(unauthorizedDb, seeded.Db.TenantId, seeded.ManagerUserId, "attendance:read")
            .Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);
        var maskedMember = unauthorized.Value!.Members.Single(m => m.EmployeeId == seeded.OnLeaveId);
        maskedMember.Status.Should().Be("absent", "not Leave-authorized - masked identically to a real no-show");
        maskedMember.Leave.Should().BeNull();
        unauthorized.Value.Summary.OnLeave.Should().Be(0);
        unauthorized.Value.Summary.Absent.Should().Be(2, "the genuinely-absent member plus the masked leave member");

        await using var authorizedDb = seeded.Db.NewContext();
        var authorized = await Handler(authorizedDb, seeded.Db.TenantId, seeded.ManagerUserId, "attendance:read", "leave:read")
            .Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);
        var revealedMember = authorized.Value!.Members.Single(m => m.EmployeeId == seeded.OnLeaveId);
        revealedMember.Status.Should().Be("on_leave");
        revealedMember.Leave.Should().NotBeNull();
        revealedMember.Leave!.LeaveTypeName.Should().Be("Annual");
        authorized.Value.Summary.OnLeave.Should().Be(1);
        authorized.Value.Summary.Absent.Should().Be(1, "only the genuinely-absent member now");
    }

    [Fact]
    public async Task Command_count_is_constant_regardless_of_team_size()
    {
        async Task<int> CountAsync(int coveredCount)
        {
            var helper = await MyTeamDb.CreateAsync();
            Guid managerUserId;
            await using (var db = helper.NewContext())
            {
                var manager = helper.AddEmployee(db);
                managerUserId = manager.UserId;
                var managerPosition = helper.AddPositionHeldBy(db, manager.Id);
                var dept = helper.AddDepartment(db);
                for (var i = 0; i < coveredCount; i++)
                {
                    var member = helper.AddEmployee(db, departmentId: dept);
                    helper.AddAttendanceRecord(db, member.Id, WorkDate,
                        actualStart: FixedNow.AddHours(-4), status: AttendanceRecord.StatusOnTime);
                }
                helper.AddCoverage(db, managerPosition, ManagementCoverageRecord.TargetDepartment, coveredDepartmentId: dept);
                helper.AddClockInPolicy(db);
                helper.GrantPermission(db, managerUserId, "attendance:read");
                await db.SaveChangesAsync();
            }

            var counter = new CountingDbCommandInterceptor();
            await using var read = helper.NewContext(counter);
            await Handler(read, helper.TenantId, managerUserId, "attendance:read")
                .Handle(new GetCoveredTeamTodayQuery(), CancellationToken.None);
            return counter.Count;
        }

        var small = await CountAsync(2);
        var large = await CountAsync(20);

        small.Should().Be(large);
    }
}
