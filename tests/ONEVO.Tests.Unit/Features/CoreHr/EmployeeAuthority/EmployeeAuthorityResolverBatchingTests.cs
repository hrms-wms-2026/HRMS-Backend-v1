using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.EmployeeAuthority;

public sealed class EmployeeAuthorityResolverBatchingTests
{
    private const string AttendanceRead = "attendance:read";

    [Fact]
    public async Task Position_coverage_resolves_all_holders_with_one_batch_call()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var le = Guid.NewGuid();
        var actor = graph.AddEmployee(le);
        var actorPosition = graph.AddPosition(le);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        var covered = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            var position = graph.AddPosition(le);
            var holder = graph.AddEmployee(le);
            graph.AddPrimaryAssignment(holder.Id, position.Id);
            graph.AddCoverage(le, actorPosition.Id, "Position", position.Id, null, ownerOrder: i + 1);
            covered.Add(holder.Id);
        }
        graph.GrantPermission(actor.UserId, AttendanceRead);

        var scope = await graph.BuildResolver().ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));

        Assert.Equal(covered.OrderBy(x => x), scope.EmployeeIds.OrderBy(x => x));
        Assert.Equal(1, graph.CallCounts.GetValueOrDefault("PositionAssignment.GetActiveHoldersByPositionIdsAsync"));
        Assert.Equal(0, graph.CallCounts.GetValueOrDefault("PositionAssignment.GetActiveHoldersAsync"));
    }

    [Fact]
    public async Task Department_coverage_expands_all_roots_with_one_call()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var le = Guid.NewGuid();
        var actor = graph.AddEmployee(le);
        var actorPosition = graph.AddPosition(le);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        var expected = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var root = graph.AddDepartment();
            var child = graph.AddDepartment(parentDepartmentId: root);
            expected.Add(graph.AddEmployee(le, departmentId: child).Id);
            graph.AddCoverage(le, actorPosition.Id, "Department", null, root, ownerOrder: i + 1);
        }
        graph.GrantPermission(actor.UserId, AttendanceRead);

        var scope = await graph.BuildResolver().ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));

        Assert.Equal(expected.OrderBy(x => x), scope.EmployeeIds.OrderBy(x => x));
        Assert.Equal(1, graph.CallCounts.GetValueOrDefault("Department.GetDescendantDepartmentIdsAsync"));
    }
}
