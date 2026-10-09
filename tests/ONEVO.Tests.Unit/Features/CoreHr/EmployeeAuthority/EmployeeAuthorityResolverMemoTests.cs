using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.EmployeeAuthority;

public sealed class EmployeeAuthorityResolverMemoTests
{
    private const string AttendanceRead = "attendance:read";
    private const string AttendanceApprove = "attendance:approve";

    private static (EmployeeAuthorityTestGraph Graph, Guid Le, ONEVO.Domain.Features.CoreHr.Entities.Employee Actor, Guid Covered) Build()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var le = Guid.NewGuid();
        var actor = graph.AddEmployee(le);
        var actorPosition = graph.AddPosition(le);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        var coveredPosition = graph.AddPosition(le);
        var covered = graph.AddEmployee(le);
        graph.AddPrimaryAssignment(covered.Id, coveredPosition.Id);
        graph.AddCoverage(le, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1);
        return (graph, le, actor, covered.Id);
    }

    [Fact]
    public async Task Second_call_in_same_scope_reuses_expansion_but_rechecks_permission()
    {
        var (graph, le, actor, covered) = Build();
        graph.GrantPermission(actor.UserId, AttendanceRead);
        graph.GrantPermission(actor.UserId, AttendanceApprove);
        var resolver = graph.BuildResolver();

        var first = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));
        var second = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.Contains(covered, first.EmployeeIds);
        Assert.Contains(covered, second.EmployeeIds);
        Assert.Equal(1, graph.CallCounts.GetValueOrDefault("Position.ListCoverageByOwnerPositionAsync"));
        Assert.Equal(1, graph.CallCounts.GetValueOrDefault("Employee.GetByUserAndLegalEntityAsync"));
        Assert.Equal(2, graph.CallCounts.GetValueOrDefault("Permission.UserHasPermissionCodeAsync"));
    }

    [Fact]
    public async Task Memo_never_grants_coverage_to_a_permission_the_actor_lacks()
    {
        var (graph, le, actor, covered) = Build();
        graph.GrantPermission(actor.UserId, AttendanceRead); // NOT attendance:approve
        var resolver = graph.BuildResolver();

        await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));
        var denied = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.DoesNotContain(covered, denied.EmployeeIds);
        Assert.Empty(denied.EmployeeIds);
    }

    [Fact]
    public async Task Memo_is_per_legal_entity()
    {
        var (graph, le, actor, _) = Build();
        graph.GrantPermission(actor.UserId, AttendanceRead);
        var otherLe = Guid.NewGuid();
        var resolver = graph.BuildResolver();

        await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));
        var other = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, otherLe, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead));

        Assert.Empty(other.EmployeeIds);
        Assert.Equal(2, graph.CallCounts.GetValueOrDefault("Employee.GetByUserAndLegalEntityAsync"));
    }

    [Fact]
    public async Task A_new_resolver_instance_starts_with_an_empty_memo()
    {
        var (graph, le, actor, _) = Build();
        graph.GrantPermission(actor.UserId, AttendanceRead);
        var request = new EmployeeAuthorityVisibilityRequest(
            actor.UserId, le, AttendanceRead, IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead);

        await graph.BuildResolver().ResolveVisibilityAsync(request);
        await graph.BuildResolver().ResolveVisibilityAsync(request);

        Assert.Equal(2, graph.CallCounts.GetValueOrDefault("Position.ListCoverageByOwnerPositionAsync"));
    }
}
