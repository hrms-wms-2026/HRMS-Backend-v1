using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Infrastructure.Persistence.Repositories.Leave.Request;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.Leave;

/// <summary>Team planner (2026-11-03, Phase A): pending leave covering a range, beside the approved read. Requires Docker.</summary>
public sealed class LeaveRequestReadRepositoryPendingTests
{
    [Fact]
    public async Task ListPendingCovering_ReturnsOnlyPendingThatOverlapTheRange()
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid employeeId, expectedId;
        await using (var db = helper.NewContext())
        {
            var employee = helper.AddEmployee(db);
            var other = helper.AddEmployee(db, lastName: "Other");
            var type = helper.AddLeaveType(db);
            employeeId = employee.Id;
            var inRange = helper.AddApprovedLeave(db, employee.Id, type, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 6));
            inRange.Status = LeaveRequestStatuses.Pending;
            expectedId = inRange.Id;
            helper.AddApprovedLeave(db, employee.Id, type, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5));
            helper.AddApprovedLeave(db, employee.Id, type, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10)).Status = LeaveRequestStatuses.Pending;
            helper.AddApprovedLeave(db, other.Id, type, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5)).Status = LeaveRequestStatuses.Pending;
            await db.SaveChangesAsync();
        }

        await using var read = helper.NewContext();
        var rows = await new EfLeaveRequestReadRepository(read).ListPendingCoveringAsync(
            helper.TenantId, [employeeId], new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8));

        var row = Assert.Single(rows);
        Assert.Equal(expectedId, row.Id);
    }
}
