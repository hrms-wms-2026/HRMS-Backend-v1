using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.Leave.Calendar.RepositoryInterfaces;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Calendar;
using Xunit;

namespace ONEVO.Tests.Integration.MyTeam;

/// <summary>Proves EmployeeVisibilityScopeMatcher stays behaviorally equivalent to
/// EfLeaveCalendarRepository's SQL filter, across every scope shape AND the boundary/edge
/// subjects called out in the review (2026-09-30): no department, no primary position, another
/// legal entity, own-employee-also-covered overlap, and an inactive/expired position assignment.
/// Requires Docker (Testcontainers PostgreSQL).</summary>
public sealed class EmployeeVisibilityScopeMatcherParityTests
{
    [Fact]
    public async Task Matcher_selects_exactly_the_employees_the_leave_calendar_SQL_selects()
    {
        var helper = await MyTeamDb.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid parentDept, coveredPosition, strangerId, noDeptId, noPositionId, otherLeId, ownWithOverlapId;
        var all = new List<Guid>();
        Guid otherLeId2;
        await using (var db = helper.NewContext())
        {
            var leaveTypeId = helper.AddLeaveType(db);

            parentDept = helper.AddDepartment(db);
            var childDept = helper.AddDepartment(db, parentDept);
            var inParent = helper.AddEmployee(db, departmentId: parentDept);
            var inChild = helper.AddEmployee(db, departmentId: childDept);

            var positionHolder = helper.AddEmployee(db);
            coveredPosition = helper.AddPositionHeldBy(db, positionHolder.Id);

            var stranger = helper.AddEmployee(db);
            strangerId = stranger.Id;

            // Clarification-4 edge subjects:
            var noDept = helper.AddEmployee(db, departmentId: null);        // no department at all
            noDeptId = noDept.Id;
            var noPosition = helper.AddEmployee(db);                       // never assigned any position
            noPositionId = noPosition.Id;

            var otherLe = new ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity { Id = Guid.NewGuid(), TenantId = helper.TenantId, Name = "Other LE" };
            db.LegalEntities.Add(otherLe);
            var inOtherLe = helper.AddEmployee(db, legalEntityId: otherLe.Id); // belongs to a DIFFERENT legal entity
            otherLeId = inOtherLe.Id;
            otherLeId2 = otherLe.Id;

            var ownWithOverlap = helper.AddEmployee(db, departmentId: parentDept); // own employee, ALSO department-covered
            ownWithOverlapId = ownWithOverlap.Id;

            var expiredPosHolder = helper.AddEmployee(db);                 // covered position, but assignment has ENDED
            helper.AddEndedPositionHeldBy(db, expiredPosHolder.Id);

            foreach (var e in new[] { inParent, inChild, positionHolder, stranger, noDept, noPosition, inOtherLe, ownWithOverlap, expiredPosHolder })
            {
                helper.AddApprovedLeave(db, e.Id, leaveTypeId, today, today);
                all.Add(e.Id);
            }
            await db.SaveChangesAsync();
        }

        var scopes = new[]
        {
            new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid> { parentDept }, new HashSet<Guid>()),
            new EmployeeVisibilityScope(false, null, new HashSet<Guid> { coveredPosition }, new HashSet<Guid>(), new HashSet<Guid>()),
            new EmployeeVisibilityScope(false, strangerId, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>()),
            new EmployeeVisibilityScope(false, null, new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid> { helper.LegalEntityId }),
            // Own-with-overlap: both own AND department coverage name the same employee.
            new EmployeeVisibilityScope(false, ownWithOverlapId, new HashSet<Guid>(), new HashSet<Guid> { parentDept }, new HashSet<Guid>()),
            // A position covered here that only the EXPIRED assignment used to hold - must select nobody via that position.
        };

        await using var read = helper.NewContext();
        var repository = new EfLeaveCalendarRepository(read);
        var employees = await read.Employees.AsNoTracking().Where(e => all.Contains(e.Id)).ToListAsync();
        var primaryPositionByEmployee = await read.PositionAssignments.AsNoTracking()
            .Where(pa => all.Contains(pa.EmployeeId) && pa.AssignmentStatus == ONEVO.Domain.Features.CoreHr.Entities.PositionAssignmentStatus.Active)
            .ToDictionaryAsync(pa => pa.EmployeeId, pa => pa.PositionId);

        foreach (var scope in scopes)
        {
            var sqlRows = await repository.ListMonthRequestsAsync(helper.TenantId, scope,
                new LeaveCalendarRequestFilter(today.AddDays(-1), today.AddDays(1), null, false));
            var sqlIds = sqlRows.Select(r => r.Request.EmployeeId).Distinct().OrderBy(x => x).ToList();

            var matcherIds = employees
                .Where(e => EmployeeVisibilityScopeMatcher.Includes(scope, e.Id,
                    primaryPositionByEmployee.TryGetValue(e.Id, out var p) ? p : null, e.DepartmentId, e.LegalEntityId))
                .Select(e => e.Id).OrderBy(x => x).ToList();

            matcherIds.Should().Equal(sqlIds, because: $"scope {scope} must select identically in SQL and in-memory");
        }

        // Edge subjects, checked directly against every non-unrestricted scope: none of them should
        // ever surface via a channel they don't actually qualify for.
        var deptOnly = scopes[0];
        EmployeeVisibilityScopeMatcher.Includes(deptOnly, noDeptId, null, null, null).Should().BeFalse("no department never matches department coverage");

        var positionOnly = scopes[1];
        EmployeeVisibilityScopeMatcher.Includes(positionOnly, noPositionId, null, null, null).Should().BeFalse("no primary position never matches position coverage");

        var companyWide = scopes[3];
        EmployeeVisibilityScopeMatcher.Includes(companyWide, otherLeId, null, null, otherLeId2).Should().BeFalse("another legal entity never matches this legal entity's company-wide coverage");
    }
}
