using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.EmployeeAuthority;

/// <summary>Covers HasAnyManagedCoverageAsync (My Team spec §9.2): a cheap existence-only probe
/// used to gate "People I manage" capability/action-item sources without paying for a full
/// ResolveVisibilityAsync expansion.</summary>
public sealed class HasAnyManagedCoverageAsyncTests
{
    private const string AttendanceApprove = "attendance:approve";

    [Fact]
    public async Task False_when_actor_lacks_the_required_permission()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        var coveredPosition = graph.AddPosition(legalEntityId);
        graph.AddCoverage(legalEntityId, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1);
        // Deliberately no GrantPermission call.
        var resolver = graph.BuildResolver();

        var result = await resolver.HasAnyManagedCoverageAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result);
    }

    [Fact]
    public async Task False_when_actor_has_permission_but_no_position_assignment()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var actor = graph.AddEmployee(legalEntityId);
        graph.GrantPermission(actor.UserId, AttendanceApprove);
        var resolver = graph.BuildResolver();

        var result = await resolver.HasAnyManagedCoverageAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result);
    }

    [Fact]
    public async Task False_when_actor_has_permission_and_a_position_but_zero_coverage_rows()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        graph.GrantPermission(actor.UserId, AttendanceApprove);
        var resolver = graph.BuildResolver();

        var result = await resolver.HasAnyManagedCoverageAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result);
    }

    [Fact]
    public async Task True_when_actor_has_permission_and_at_least_one_active_coverage_row()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        var coveredPosition = graph.AddPosition(legalEntityId);
        graph.AddCoverage(legalEntityId, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, AttendanceApprove);
        var resolver = graph.BuildResolver();

        var result = await resolver.HasAnyManagedCoverageAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result);
    }

    [Fact]
    public async Task True_via_department_coverage_row_too_not_only_position_coverage()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        var departmentId = graph.AddDepartment();
        graph.AddCoverage(legalEntityId, actorPosition.Id, "Department", null, departmentId, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, AttendanceApprove);
        var resolver = graph.BuildResolver();

        var result = await resolver.HasAnyManagedCoverageAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result);
    }

    [Fact]
    public async Task False_when_the_only_coverage_row_is_inactive()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);
        var coveredPosition = graph.AddPosition(legalEntityId);
        graph.AddCoverage(legalEntityId, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1, active: false);
        graph.GrantPermission(actor.UserId, AttendanceApprove);
        var resolver = graph.BuildResolver();

        var result = await resolver.HasAnyManagedCoverageAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result);
    }

    [Fact]
    public async Task False_when_the_actor_has_no_employee_record_in_this_legal_entity()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var actor = graph.AddEmployee(otherLegalEntityId); // exists, but not in the requested legal entity
        graph.GrantPermission(actor.UserId, AttendanceApprove);
        var resolver = graph.BuildResolver();

        var result = await resolver.HasAnyManagedCoverageAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, AttendanceApprove, IncludeSelf: false, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result);
    }
}
