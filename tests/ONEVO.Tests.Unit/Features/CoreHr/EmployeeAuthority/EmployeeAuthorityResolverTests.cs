using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.EmployeeAuthority;

/// <summary>
/// Covers the 28 required unit scenarios from EMPLOYEE_AUTHORITY_RESOLVER_BACKEND_PART0
/// (11 visibility + 17 approval routing). Test numbers in each Fact name/comment match the task's
/// numbered scenario list so the report can cite them directly.
/// </summary>
public sealed class EmployeeAuthorityResolverTests
{
    private const string EmployeesRead = "employees:read";
    private const string AttendanceApprove = "attendance:approve";

    // ---------------------------------------------------------------------
    // Visibility (1-11)
    // ---------------------------------------------------------------------

    [Fact] // 1. Actor sees self when IncludeSelf = true.
    public async Task Visibility_IncludesSelf_WhenRequested()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var actor = graph.AddEmployee(legalEntityId);
        var resolver = graph.BuildResolver();

        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, EmployeesRead, IncludeSelf: true, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.True(scope.IncludesSelf);
        Assert.Equal(new[] { actor.Id }, scope.EmployeeIds);
    }

    [Fact] // 2. Actor does not see self when IncludeSelf = false and no coverage.
    public async Task Visibility_ExcludesSelf_WhenNotRequestedAndNoCoverage()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var actor = graph.AddEmployee(legalEntityId);
        var resolver = graph.BuildResolver();

        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.False(scope.IncludesSelf);
        Assert.Empty(scope.EmployeeIds);
    }

    [Fact] // 3. Actor with coverage and required permission sees direct covered employees.
    public async Task Visibility_SeesDirectCoveredEmployee_WithPermission()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);

        var coveredPosition = graph.AddPosition(legalEntityId);
        var covered = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(covered.Id, coveredPosition.Id);

        graph.AddCoverage(legalEntityId, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, EmployeesRead);

        var resolver = graph.BuildResolver();
        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.Contains(covered.Id, scope.EmployeeIds);
    }

    [Fact] // 4. Actor with coverage and permission sees transitive employees under covered reporting hierarchy.
    public async Task Visibility_SeesTransitiveEmployees_UnderCoveredPosition()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);

        var gmPosition = graph.AddPosition(legalEntityId);
        var gm = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(gm.Id, gmPosition.Id);

        var engineer = graph.AddEmployee(legalEntityId);
        graph.SetManager(engineer.Id, gm.Id);

        graph.AddCoverage(legalEntityId, actorPosition.Id, "Position", gmPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, EmployeesRead);

        var resolver = graph.BuildResolver();
        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.Contains(gm.Id, scope.EmployeeIds);
        Assert.Contains(engineer.Id, scope.EmployeeIds);
    }

    [Fact] // 5. Actor without required permission does not see covered employees.
    public async Task Visibility_DoesNotSeeCoveredEmployees_WithoutPermission()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);

        var coveredPosition = graph.AddPosition(legalEntityId);
        var covered = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(covered.Id, coveredPosition.Id);

        graph.AddCoverage(legalEntityId, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1);
        // No GrantPermission call.

        var resolver = graph.BuildResolver();
        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.Empty(scope.EmployeeIds);
    }

    [Fact] // 6. Position coverage visibility is legal-entity scoped.
    public async Task Visibility_PositionCoverage_IsLegalEntityScoped()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntity1 = Guid.NewGuid();
        var legalEntity2 = Guid.NewGuid();

        var actor = graph.AddEmployee(legalEntity1);
        var actorPosition = graph.AddPosition(legalEntity1);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);

        var coveredPosition = graph.AddPosition(legalEntity1);
        var covered = graph.AddEmployee(legalEntity1);
        graph.AddPrimaryAssignment(covered.Id, coveredPosition.Id);

        // Data mismatch: the coverage row's own LegalEntityId does not match the position's
        // actual legal entity - ListActiveCoverageByOwnerPositionAsync must filter on the
        // requested legal entity, not merely on OwnerPositionId, so this row must never surface.
        graph.AddCoverage(legalEntity2, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, EmployeesRead);

        var resolver = graph.BuildResolver();
        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntity1, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.DoesNotContain(covered.Id, scope.EmployeeIds);
    }

    [Fact] // 7. Department coverage visibility includes employees in child departments if hierarchy exists.
    public async Task Visibility_DepartmentCoverage_IncludesChildDepartments()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);

        var rootDept = graph.AddDepartment();
        var childDept = graph.AddDepartment(parentDepartmentId: rootDept);
        var employeeInChild = graph.AddEmployee(legalEntityId, departmentId: childDept);

        graph.AddCoverage(legalEntityId, actorPosition.Id, "Department", null, rootDept, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, EmployeesRead);

        var resolver = graph.BuildResolver();
        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.Contains(employeeInChild.Id, scope.EmployeeIds);
    }

    [Fact] // 8. Inactive employees are excluded.
    public async Task Visibility_ExcludesInactiveEmployees()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);

        var coveredPosition = graph.AddPosition(legalEntityId);
        var inactiveCovered = graph.AddEmployee(legalEntityId, active: false);
        graph.AddPrimaryAssignment(inactiveCovered.Id, coveredPosition.Id);

        graph.AddCoverage(legalEntityId, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, EmployeesRead);

        var resolver = graph.BuildResolver();
        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.DoesNotContain(inactiveCovered.Id, scope.EmployeeIds);
    }

    [Fact] // 9. Inactive position assignments are excluded.
    public async Task Visibility_ExcludesInactivePositionAssignments()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);

        var coveredPosition = graph.AddPosition(legalEntityId);
        var formerHolder = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(formerHolder.Id, coveredPosition.Id, active: false);

        graph.AddCoverage(legalEntityId, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, EmployeesRead);

        var resolver = graph.BuildResolver();
        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.DoesNotContain(formerHolder.Id, scope.EmployeeIds);
    }

    [Fact] // 10. Cross-tenant employees are excluded.
    public async Task Visibility_ExcludesCrossTenantEmployees()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);

        var rootDept = graph.AddDepartment();
        var foreignTenantEmployee = graph.AddEmployee(
            legalEntityId, departmentId: rootDept, tenantIdOverride: Guid.NewGuid());

        graph.AddCoverage(legalEntityId, actorPosition.Id, "Department", null, rootDept, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, EmployeesRead);

        var resolver = graph.BuildResolver();
        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.DoesNotContain(foreignTenantEmployee.Id, scope.EmployeeIds);
    }

    [Fact] // 11. Cross-legal-entity employees are excluded.
    public async Task Visibility_ExcludesCrossLegalEntityEmployees()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntity1 = Guid.NewGuid();
        var legalEntity2 = Guid.NewGuid();

        var actor = graph.AddEmployee(legalEntity1);
        var actorPosition = graph.AddPosition(legalEntity1);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);

        var rootDept = graph.AddDepartment();
        // Same tenant, but the employee's own legal entity differs from the requested one even
        // though its DepartmentId happens to collide with the covered department id.
        var otherLegalEntityEmployee = graph.AddEmployee(legalEntity2, departmentId: rootDept);

        graph.AddCoverage(legalEntity1, actorPosition.Id, "Department", null, rootDept, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, EmployeesRead);

        var resolver = graph.BuildResolver();
        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntity1, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.DoesNotContain(otherLegalEntityEmployee.Id, scope.EmployeeIds);
    }

    [Fact] // Manual coverage owner eligibility correction: visibility via manual coverage works
           // even when the covered position/holder is entirely outside the actor's reporting
           // line - there is no reporting-line requirement for visibility, only for the
           // reporting-line fallback tier of approval routing.
    public async Task Visibility_ManualCoverage_OutsideReportingLine_Works()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var actor = graph.AddEmployee(legalEntityId);
        var actorPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(actor.Id, actorPosition.Id);

        // No SetManager relationship between actor and covered at all - genuinely unrelated
        // positions in the org chart, connected only by a manually configured coverage record.
        var coveredPosition = graph.AddPosition(legalEntityId);
        var covered = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(covered.Id, coveredPosition.Id);

        graph.AddCoverage(legalEntityId, actorPosition.Id, "Position", coveredPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(actor.UserId, EmployeesRead);

        var resolver = graph.BuildResolver();
        var scope = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            actor.UserId, legalEntityId, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));

        Assert.Contains(covered.Id, scope.EmployeeIds);
    }

    // ---------------------------------------------------------------------
    // Approval routing (12-28)
    // ---------------------------------------------------------------------

    [Fact] // 12. Position coverage primary owner with permission is selected.
    public async Task Approval_SelectsPrimaryPositionCoverageOwner_WithPermission()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var ownerPosition = graph.AddPosition(legalEntityId);
        var owner = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(owner.Id, ownerPosition.Id);
        graph.SetManager(subject.Id, owner.Id);

        graph.AddCoverage(legalEntityId, ownerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(owner.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(owner.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(EmployeeApprovalRouteSource.PositionCoverage, result.Value.Source);
        Assert.Equal(1, result.Value.OwnerOrder);
    }

    [Fact] // 13. If primary lacks permission, Backup 1 with permission is selected.
    public async Task Approval_FallsBackToBackup1_WhenPrimaryLacksPermission()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var primaryPosition = graph.AddPosition(legalEntityId);
        var primary = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(primary.Id, primaryPosition.Id);
        graph.SetManager(subject.Id, primary.Id);

        var backup1Position = graph.AddPosition(legalEntityId);
        var backup1 = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(backup1.Id, backup1Position.Id);
        graph.SetManager(primary.Id, backup1.Id);

        graph.AddCoverage(legalEntityId, primaryPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.AddCoverage(legalEntityId, backup1Position.Id, "Position", subjectPosition.Id, null, ownerOrder: 2);
        graph.GrantPermission(backup1.UserId, AttendanceApprove); // primary NOT granted

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(backup1.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(2, result.Value.OwnerOrder);
    }

    [Fact] // 14. If Backup 1 lacks permission, Backup 2 with permission is selected.
    public async Task Approval_FallsBackToBackup2_WhenBackup1AlsoLacksPermission()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var chain = BuildOwnerChain(graph, legalEntityId, subject.Id, subjectPosition.Id, levels: 3);

        graph.GrantPermission(chain[2].Employee.UserId, AttendanceApprove); // only level 3 (Backup 2)

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(chain[2].Employee.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(3, result.Value.OwnerOrder);
    }

    [Fact] // 15. N-level backup works, not hardcoded to 2 or 3.
    public async Task Approval_SupportsArbitraryNumberOfBackupLevels()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        const int levels = 6;
        var chain = BuildOwnerChain(graph, legalEntityId, subject.Id, subjectPosition.Id, levels);

        graph.GrantPermission(chain[^1].Employee.UserId, AttendanceApprove); // only the last (level 6) owner

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(chain[^1].Employee.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(levels, result.Value.OwnerOrder);
    }

    [Fact] // 16. Position coverage is checked before department coverage.
    public async Task Approval_PositionCoverage_TakesPriorityOverDepartmentCoverage()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var department = graph.AddDepartment();

        var subject = graph.AddEmployee(legalEntityId, departmentId: department);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var positionOwnerPosition = graph.AddPosition(legalEntityId);
        var positionOwner = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(positionOwner.Id, positionOwnerPosition.Id);
        graph.SetManager(subject.Id, positionOwner.Id);

        var deptOwnerPosition = graph.AddPosition(legalEntityId);
        var deptOwner = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(deptOwner.Id, deptOwnerPosition.Id);
        graph.SetManager(positionOwner.Id, deptOwner.Id);

        graph.AddCoverage(legalEntityId, positionOwnerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.AddCoverage(legalEntityId, deptOwnerPosition.Id, "Department", null, department, ownerOrder: 1);
        graph.GrantPermission(positionOwner.UserId, AttendanceApprove);
        graph.GrantPermission(deptOwner.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(positionOwner.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(EmployeeApprovalRouteSource.PositionCoverage, result.Value.Source);
    }

    [Fact] // 17. Department coverage is checked before reporting-line fallback.
    public async Task Approval_DepartmentCoverage_TakesPriorityOverReportingLine()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var department = graph.AddDepartment();

        var subject = graph.AddEmployee(legalEntityId, departmentId: department);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var directManager = graph.AddEmployee(legalEntityId); // no permission - would fail reporting-line tier 1 hop
        graph.SetManager(subject.Id, directManager.Id);

        var deptOwnerPosition = graph.AddPosition(legalEntityId);
        var deptOwner = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(deptOwner.Id, deptOwnerPosition.Id);
        graph.SetManager(directManager.Id, deptOwner.Id);

        graph.AddCoverage(legalEntityId, deptOwnerPosition.Id, "Department", null, department, ownerOrder: 1);
        graph.GrantPermission(deptOwner.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeApprovalRouteSource.DepartmentCoverage, result.Value!.Source);
        Assert.Equal(deptOwner.Id, result.Value.ApproverEmployeeId);
    }

    [Fact] // 18. Reporting-line fallback walks upward only.
    public async Task Approval_ReportingLineFallback_WalksUpward()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var directManager = graph.AddEmployee(legalEntityId); // no permission
        var skipLevelManager = graph.AddEmployee(legalEntityId); // has permission
        var skipLevelPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(skipLevelManager.Id, skipLevelPosition.Id);
        graph.SetManager(subject.Id, directManager.Id);
        graph.SetManager(directManager.Id, skipLevelManager.Id);

        graph.GrantPermission(skipLevelManager.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(EmployeeApprovalRouteSource.ReportingLine, result.Value!.Source);
        Assert.Equal(skipLevelManager.Id, result.Value.ApproverEmployeeId);
    }

    [Fact] // 19. Subordinate with approval permission is never selected for manager's request.
    public async Task Approval_NeverSelectsSubordinate()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var subordinatePosition = graph.AddPosition(legalEntityId);
        var subordinate = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(subordinate.Id, subordinatePosition.Id);
        graph.SetManager(subordinate.Id, subject.Id); // subordinate reports to subject

        // Misconfigured coverage: subordinate's position is (incorrectly) named as owner.
        graph.AddCoverage(legalEntityId, subordinatePosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(subordinate.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result.IsSuccess);
    }

    [Fact] // 20 (corrected). Sibling with approval permission is still rejected in
           // reporting-line fallback specifically - a sibling never appears in the subject's
           // ancestor chain, so tier 3 structurally cannot reach them, regardless of permission.
           // (Superseded scenario: a sibling *manually configured as a coverage owner* is no
           // longer rejected - see Approval_PositionCoverageOwner_OutsideReportingLine_IsSelected.
           // Manual coverage is authoritative and a sibling is not a subordinate of the subject,
           // so rejecting them there would contradict the corrected product rule.)
    public async Task Approval_ReportingLineFallback_NeverSelectsSibling()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var sharedManager = graph.AddEmployee(legalEntityId);
        var sharedManagerPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(sharedManager.Id, sharedManagerPosition.Id);
        graph.SetManager(subject.Id, sharedManager.Id);

        var siblingPosition = graph.AddPosition(legalEntityId);
        var sibling = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(sibling.Id, siblingPosition.Id);
        graph.SetManager(sibling.Id, sharedManager.Id);

        // No coverage record at all - purely testing that tier 3 (reporting-line fallback) never
        // reaches a sibling, even though the sibling has the required permission.
        graph.GrantPermission(sibling.UserId, AttendanceApprove);
        graph.GrantPermission(sharedManager.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(sharedManager.Id, result.Value!.ApproverEmployeeId);
        Assert.NotEqual(sibling.Id, result.Value.ApproverEmployeeId);
        Assert.Equal(EmployeeApprovalRouteSource.ReportingLine, result.Value.Source);
    }

    [Fact] // Manual coverage owner eligibility correction: position coverage owner outside the
           // subject's reporting line (e.g. a sibling, or an unrelated HR business partner) with
           // the required permission is selected - manual coverage is authoritative and is not
           // required to be a reporting-line ancestor of the subject.
    public async Task Approval_PositionCoverageOwner_OutsideReportingLine_IsSelected()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        // HR Manager: no SetManager relationship to subject in either direction - genuinely
        // outside the subject's reporting line, only connected via manual coverage.
        var hrManagerPosition = graph.AddPosition(legalEntityId);
        var hrManager = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(hrManager.Id, hrManagerPosition.Id);

        graph.AddCoverage(legalEntityId, hrManagerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(hrManager.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(hrManager.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(EmployeeApprovalRouteSource.PositionCoverage, result.Value.Source);
        Assert.Equal(1, result.Value.OwnerOrder);
    }

    [Fact] // Manual coverage owner eligibility correction: department coverage owner outside the
           // subject's reporting line with the required permission is selected.
    public async Task Approval_DepartmentCoverageOwner_OutsideReportingLine_IsSelected()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var department = graph.AddDepartment();

        var subject = graph.AddEmployee(legalEntityId, departmentId: department);

        var hrManagerPosition = graph.AddPosition(legalEntityId);
        var hrManager = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(hrManager.Id, hrManagerPosition.Id);

        graph.AddCoverage(legalEntityId, hrManagerPosition.Id, "Department", null, department, ownerOrder: 1);
        graph.GrantPermission(hrManager.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(hrManager.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(EmployeeApprovalRouteSource.DepartmentCoverage, result.Value.Source);
    }

    [Fact] // Manual coverage owner eligibility correction: backup owner outside the reporting
           // line is selected when the primary owner (also outside the reporting line) lacks the
           // required permission.
    public async Task Approval_BackupOwner_OutsideReportingLine_IsSelected_WhenPrimaryLacksPermission()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var primaryPosition = graph.AddPosition(legalEntityId);
        var primary = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(primary.Id, primaryPosition.Id);
        // No SetManager for primary - unrelated to subject's reporting line.

        var backupPosition = graph.AddPosition(legalEntityId);
        var backup = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(backup.Id, backupPosition.Id);
        // No SetManager for backup either.

        graph.AddCoverage(legalEntityId, primaryPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.AddCoverage(legalEntityId, backupPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 2);
        graph.GrantPermission(backup.UserId, AttendanceApprove); // primary NOT granted

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(backup.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(2, result.Value.OwnerOrder);
    }

    [Fact] // Manual coverage owner eligibility correction: owner order is still respected even
           // when none of the configured owners are reporting-line ancestors of the subject.
    public async Task Approval_OwnersOutsideReportingLine_StillRespectOwnerOrder()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var owners = new List<ONEVO.Domain.Features.CoreHr.Entities.Employee>();
        for (var level = 1; level <= 3; level++)
        {
            var position = graph.AddPosition(legalEntityId);
            var owner = graph.AddEmployee(legalEntityId);
            graph.AddPrimaryAssignment(owner.Id, position.Id);
            // Deliberately no SetManager - none of these owners are ancestors of subject.
            graph.AddCoverage(legalEntityId, position.Id, "Position", subjectPosition.Id, null, ownerOrder: level);
            owners.Add(owner);
        }

        graph.GrantPermission(owners[0].UserId, AttendanceApprove);
        graph.GrantPermission(owners[2].UserId, AttendanceApprove); // level 3 also has permission

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        // OwnerOrder 1 (owners[0]) has permission, so it must win over OwnerOrder 3 even though
        // both are equally "outside the reporting line" - order is still authoritative.
        Assert.True(result.IsSuccess);
        Assert.Equal(owners[0].Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(1, result.Value.OwnerOrder);
    }

    [Fact] // Manual coverage owner eligibility correction: a coverage owner who is the subject
           // employee themselves is rejected, even though nothing else in the guard chain would
           // catch it (same tenant, same legal entity, active, holds the permission).
    public async Task Approval_CoverageOwner_WhoIsSubjectThemselves_IsRejected()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var department = graph.AddDepartment();

        var subject = graph.AddEmployee(legalEntityId, departmentId: department);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        // Misconfigured coverage: the subject's own position is named as the department's owner.
        graph.AddCoverage(legalEntityId, subjectPosition.Id, "Department", null, department, ownerOrder: 1);
        graph.GrantPermission(subject.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result.IsSuccess);
    }

    [Fact] // 21. If immediate manager lacks permission, resolver continues upward.
    public async Task Approval_ContinuesUpward_WhenImmediateManagerLacksPermission()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var projectManager = graph.AddEmployee(legalEntityId);
        var generalManager = graph.AddEmployee(legalEntityId); // lacks permission
        var ceo = graph.AddEmployee(legalEntityId); // has permission
        var ceoPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(ceo.Id, ceoPosition.Id);
        graph.SetManager(projectManager.Id, generalManager.Id);
        graph.SetManager(generalManager.Id, ceo.Id);

        graph.GrantPermission(ceo.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            projectManager.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(ceo.Id, result.Value!.ApproverEmployeeId);
    }

    [Fact] // 22. If no eligible approver exists, returns business failure.
    public async Task Approval_ReturnsBusinessFailure_WhenNoEligibleApproverExists()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var manager = graph.AddEmployee(legalEntityId); // no permission granted
        graph.SetManager(subject.Id, manager.Id);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
        Assert.Equal("No eligible approver was found for this employee and action.", result.Error);
    }

    [Fact] // 23. Does not route to subject employee/self.
    public async Task Approval_NeverRoutesToSubjectItself()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        graph.GrantPermission(subject.UserId, AttendanceApprove); // subject "has" the permission itself

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result.IsSuccess);
    }

    [Fact] // 24. Ignores inactive coverage records.
    public async Task Approval_IgnoresInactiveCoverageRecords()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var inactiveOwnerPosition = graph.AddPosition(legalEntityId);
        var inactiveOwner = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(inactiveOwner.Id, inactiveOwnerPosition.Id);
        graph.SetManager(subject.Id, inactiveOwner.Id);
        graph.GrantPermission(inactiveOwner.UserId, AttendanceApprove);

        var activeOwnerPosition = graph.AddPosition(legalEntityId);
        var activeOwner = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(activeOwner.Id, activeOwnerPosition.Id);
        graph.SetManager(inactiveOwner.Id, activeOwner.Id);
        graph.GrantPermission(activeOwner.UserId, AttendanceApprove);

        graph.AddCoverage(legalEntityId, inactiveOwnerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1, active: false);
        graph.AddCoverage(legalEntityId, activeOwnerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 2, active: true);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(activeOwner.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(2, result.Value.OwnerOrder);
    }

    [Fact] // 25. Ignores inactive owner positions.
    public async Task Approval_IgnoresInactiveOwnerPositions()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var inactiveOwnerPosition = graph.AddPosition(legalEntityId, isActive: false);
        var ownerOfInactivePosition = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(ownerOfInactivePosition.Id, inactiveOwnerPosition.Id);
        graph.SetManager(subject.Id, ownerOfInactivePosition.Id);
        graph.GrantPermission(ownerOfInactivePosition.UserId, AttendanceApprove);

        var activeOwnerPosition = graph.AddPosition(legalEntityId, isActive: true);
        var activeOwner = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(activeOwner.Id, activeOwnerPosition.Id);
        graph.SetManager(ownerOfInactivePosition.Id, activeOwner.Id);
        graph.GrantPermission(activeOwner.UserId, AttendanceApprove);

        graph.AddCoverage(legalEntityId, inactiveOwnerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.AddCoverage(legalEntityId, activeOwnerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 2);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(activeOwner.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(2, result.Value.OwnerOrder);
    }

    [Fact] // 26. Ignores inactive owner employee assignments.
    public async Task Approval_IgnoresInactiveOwnerAssignments()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var vacatedOwnerPosition = graph.AddPosition(legalEntityId);
        var formerOwner = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(formerOwner.Id, vacatedOwnerPosition.Id, active: false);
        graph.SetManager(subject.Id, formerOwner.Id);
        graph.GrantPermission(formerOwner.UserId, AttendanceApprove);

        var activeOwnerPosition = graph.AddPosition(legalEntityId);
        var activeOwner = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(activeOwner.Id, activeOwnerPosition.Id);
        graph.SetManager(formerOwner.Id, activeOwner.Id);
        graph.GrantPermission(activeOwner.UserId, AttendanceApprove);

        graph.AddCoverage(legalEntityId, vacatedOwnerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.AddCoverage(legalEntityId, activeOwnerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 2);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.True(result.IsSuccess);
        Assert.Equal(activeOwner.Id, result.Value!.ApproverEmployeeId);
        Assert.Equal(2, result.Value.OwnerOrder);
    }

    [Fact] // 27. Cross-tenant approver is not selected.
    public async Task Approval_NeverSelectsCrossTenantApprover()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var foreignManager = graph.AddEmployee(legalEntityId, tenantIdOverride: Guid.NewGuid());
        graph.SetManager(subject.Id, foreignManager.Id);
        graph.GrantPermission(foreignManager.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result.IsSuccess);
    }

    [Fact] // 28. Cross-legal-entity approver is not selected.
    public async Task Approval_NeverSelectsCrossLegalEntityApprover()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntity1 = Guid.NewGuid();
        var legalEntity2 = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntity1);
        var otherEntityManager = graph.AddEmployee(legalEntity2);
        graph.SetManager(subject.Id, otherEntityManager.Id);
        graph.GrantPermission(otherEntityManager.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntity1, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result.IsSuccess);
    }

    [Fact] // Manual coverage owner eligibility correction: a coverage owner (not merely a
           // reporting-line ancestor) who holds the required permission but sits in a different
           // legal entity is still never selected - the coverage-tier chokepoint
           // (ListActiveEmployeeIdsByIdsAsync) enforces legal-entity isolation independently of
           // the reporting-line-ancestor guard that tier 1/2 no longer applies.
    public async Task Approval_CoverageOwner_InDifferentLegalEntity_IsRejected()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntity1 = Guid.NewGuid();
        var legalEntity2 = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntity1);
        var subjectPosition = graph.AddPosition(legalEntity1);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        // Coverage owner has the permission and no reporting-line relationship to subject at all
        // (irrelevant now), but belongs to a different legal entity.
        var otherEntityOwnerPosition = graph.AddPosition(legalEntity2);
        var otherEntityOwner = graph.AddEmployee(legalEntity2);
        graph.AddPrimaryAssignment(otherEntityOwner.Id, otherEntityOwnerPosition.Id);

        graph.AddCoverage(legalEntity1, otherEntityOwnerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(otherEntityOwner.UserId, AttendanceApprove);

        var resolver = graph.BuildResolver();
        var result = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            subject.Id, legalEntity1, AttendanceApprove, EmployeeAuthorityPurpose.AttendanceCorrectionApproval));

        Assert.False(result.IsSuccess);
    }

    // ---------------------------------------------------------------------
    // Approval-inbox scope (ResolveApprovalInboxScopeAsync) - Part 1 Final Hardening
    // ---------------------------------------------------------------------

    [Fact] // Inbox scope: unauthenticated caller fails closed (empty result).
    public async Task InboxScope_UnauthenticatedReviewer_FailsClosed()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var candidate = graph.AddEmployee(legalEntityId).Id;
        var resolver = graph.BuildResolver(isAuthenticated: false);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { candidate }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: reviewer with no active employee row in the requested legal entity fails closed.
    public async Task InboxScope_ReviewerWithoutActiveEmployeeInLegalEntity_FailsClosed()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var candidate = graph.AddEmployee(legalEntityId).Id;
        var reviewerUserId = Guid.NewGuid();
        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { candidate }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: reviewer without the required permission receives no candidates.
    public async Task InboxScope_ReviewerWithoutRequiredPermission_FailsClosed()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var candidate = graph.AddEmployee(legalEntityId).Id;
        var reviewerUserId = Guid.NewGuid();
        graph.AddEmployee(legalEntityId, userId: reviewerUserId); // active employee, no permission granted
        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { candidate }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: for a rich fixture, the batch result set matches calling ResolveApproverAsync
           // per candidate and keeping only exact matches to the reviewer - the literal equivalence
           // requirement this whole correction is built around.
    public async Task InboxScope_MatchesPerCandidateResolveApproverAsync_OverRichFixture()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var reviewerUserId = Guid.NewGuid();

        var reviewerPosition = graph.AddPosition(legalEntityId);
        var reviewerHolder = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewerHolder.Id, reviewerPosition.Id);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var otherUserId = Guid.NewGuid();
        var otherPosition = graph.AddPosition(legalEntityId);
        var otherHolder = graph.AddEmployee(legalEntityId, userId: otherUserId);
        graph.AddPrimaryAssignment(otherHolder.Id, otherPosition.Id);
        graph.GrantPermission(otherUserId, AttendanceApprove);

        var department = graph.AddDepartment();

        var candidateIds = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            var candidatePosition = graph.AddPosition(legalEntityId);
            var candidate = graph.AddEmployee(legalEntityId, departmentId: i % 4 == 2 ? department : null);
            graph.AddPrimaryAssignment(candidate.Id, candidatePosition.Id);
            candidateIds.Add(candidate.Id);

            switch (i % 4)
            {
                case 0: // position coverage -> reviewer
                    graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Position", candidatePosition.Id, null, ownerOrder: 1);
                    break;
                case 1: // position coverage -> someone else
                    graph.AddCoverage(legalEntityId, otherPosition.Id, "Position", candidatePosition.Id, null, ownerOrder: 1);
                    break;
                case 2: // department coverage -> reviewer
                    graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Department", null, department, ownerOrder: 1);
                    break;
                case 3: // no coverage, no manager -> unroutable
                    break;
            }
        }

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var expected = new List<Guid>();
        foreach (var candidateId in candidateIds)
        {
            var route = await resolver.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                candidateId, legalEntityId, AttendanceApprove, EmployeeAuthorityPurpose.WorkAreaChangeApproval),
                CancellationToken.None);
            if (route.IsSuccess && route.Value?.ApproverUserId == reviewerUserId)
                expected.Add(candidateId);
        }

        var actual = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, candidateIds),
            CancellationToken.None);

        Assert.Equal(expected.OrderBy(x => x).ToList(), actual.OrderBy(x => x).ToList());
    }

    [Fact] // Inbox scope: empty candidate set returns empty without a matching route.
    public async Task InboxScope_EmptyCandidateSet_ReturnsEmpty()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var reviewerUserId = Guid.NewGuid();
        graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);
        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, Array.Empty<Guid>()),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: reviewer identity comes from ICurrentUser, not any field on the request
           // (EmployeeApprovalInboxScopeRequest no longer carries a reviewer id at all).
    public async Task InboxScope_ReviewerIdentityComesFromCurrentUser_NotRequest()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var reviewerUserId = Guid.NewGuid();
        var reviewerPosition = graph.AddPosition(legalEntityId);
        var reviewer = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewer.Id, reviewerPosition.Id);
        graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Contains(subject.Id, result);
    }

    [Fact] // Inbox scope: candidate whose position-coverage primary owner is the reviewer is included.
    public async Task InboxScope_PositionCoveragePrimaryOwner_IsIncluded()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var reviewerUserId = Guid.NewGuid();
        var reviewerPosition = graph.AddPosition(legalEntityId);
        var reviewer = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewer.Id, reviewerPosition.Id);
        graph.SetManager(subject.Id, reviewer.Id);
        graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Equal(new[] { subject.Id }, result);
    }

    [Fact] // Inbox scope: candidate whose position-coverage backup owner is the reviewer is included
           // when the primary owner lacks permission.
    public async Task InboxScope_PositionCoverageBackupOwner_IsIncludedWhenEarlierLevelsUnavailable()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var chain = BuildOwnerChain(graph, legalEntityId, subject.Id, subjectPosition.Id, levels: 2);
        var reviewerUserId = chain[1].Employee.UserId;
        graph.GrantPermission(reviewerUserId, AttendanceApprove); // only level 2 (Backup 1) has permission

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Equal(new[] { subject.Id }, result);
    }

    [Fact] // Inbox scope: candidate whose department-coverage owner is the reviewer is included.
    public async Task InboxScope_DepartmentCoverageOwner_IsIncluded()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var department = graph.AddDepartment();

        var subject = graph.AddEmployee(legalEntityId, departmentId: department);

        var reviewerUserId = Guid.NewGuid();
        var reviewerPosition = graph.AddPosition(legalEntityId);
        var reviewer = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewer.Id, reviewerPosition.Id);
        graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Department", null, department, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Equal(new[] { subject.Id }, result);
    }

    [Fact] // Inbox scope: a manual coverage owner outside the subject's reporting line is eligible -
           // manual coverage is authoritative and not required to be a reporting-line ancestor.
    public async Task InboxScope_ManualCoverageOwnerOutsideReportingLine_IsEligible()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var reviewerUserId = Guid.NewGuid();
        var hrManagerPosition = graph.AddPosition(legalEntityId);
        var hrManager = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(hrManager.Id, hrManagerPosition.Id); // no SetManager relationship to subject
        graph.AddCoverage(legalEntityId, hrManagerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Equal(new[] { subject.Id }, result);
    }

    [Fact] // Inbox scope: candidate whose upward reporting-line approver is the reviewer is included
           // when no coverage tier resolves anyone.
    public async Task InboxScope_UpwardReportingLineApprover_IsIncluded()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var reviewerUserId = Guid.NewGuid();
        var managerPosition = graph.AddPosition(legalEntityId);
        var manager = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(manager.Id, managerPosition.Id);
        graph.SetManager(subject.Id, manager.Id);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Equal(new[] { subject.Id }, result);
    }

    [Fact] // Inbox scope: a candidate for whom a different user is the exact approver is excluded.
    public async Task InboxScope_DifferentExactApprover_IsExcluded()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var otherUserId = Guid.NewGuid();
        var ownerPosition = graph.AddPosition(legalEntityId);
        var owner = graph.AddEmployee(legalEntityId, userId: otherUserId);
        graph.AddPrimaryAssignment(owner.Id, ownerPosition.Id);
        graph.SetManager(subject.Id, owner.Id);
        graph.AddCoverage(legalEntityId, ownerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(otherUserId, AttendanceApprove);

        var reviewerUserId = Guid.NewGuid();
        graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: a candidate merely visible to the reviewer (company-wide coverage) but not
           // exactly approvable (ResolveApproverAsync only checks Position/Department coverage and
           // the reporting line, never company-wide coverage) is excluded.
    public async Task InboxScope_MerelyVisibleCandidate_IsExcluded()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        // No primary assignment, no department, no manager - not routable to anyone.

        var reviewerUserId = Guid.NewGuid();
        var reviewerPosition = graph.AddPosition(legalEntityId);
        var reviewer = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewer.Id, reviewerPosition.Id);
        graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Company", null, null, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, EmployeesRead);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var visibility = await resolver.ResolveVisibilityAsync(new EmployeeAuthorityVisibilityRequest(
            reviewerUserId, legalEntityId, EmployeesRead, IncludeSelf: false, EmployeeAuthorityPurpose.EmployeeListRead));
        Assert.Contains(subject.Id, visibility.EmployeeIds); // sanity: company-wide coverage makes them visible

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.DoesNotContain(subject.Id, result);
    }

    [Fact] // Inbox scope: subject/self is excluded even if self-covering.
    public async Task InboxScope_SubjectItself_IsExcluded()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var reviewerUserId = Guid.NewGuid();

        var reviewerPosition = graph.AddPosition(legalEntityId);
        var reviewer = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewer.Id, reviewerPosition.Id);
        graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Position", reviewerPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { reviewer.Id }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: a reviewer who is a subordinate of the subject is excluded even if
           // coverage would otherwise resolve to them (reverse-approval guard).
    public async Task InboxScope_ReviewerWhoIsSubordinateOfSubject_IsExcluded()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var reviewerUserId = Guid.NewGuid();
        var subordinatePosition = graph.AddPosition(legalEntityId);
        var subordinate = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(subordinate.Id, subordinatePosition.Id);
        graph.SetManager(subordinate.Id, subject.Id); // reviewer reports to the subject
        graph.AddCoverage(legalEntityId, subordinatePosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: a candidate belonging to a different tenant is excluded.
    public async Task InboxScope_CrossTenantCandidate_IsExcluded()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var reviewerUserId = Guid.NewGuid();
        graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var otherTenantSubject = graph.AddEmployee(legalEntityId, tenantIdOverride: Guid.NewGuid());

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { otherTenantSubject.Id }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: a candidate in a different legal entity than requested is excluded.
    public async Task InboxScope_CrossLegalEntityCandidate_IsExcluded()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var otherLegalEntityId = Guid.NewGuid();
        var reviewerUserId = Guid.NewGuid();
        graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var otherEntitySubject = graph.AddEmployee(otherLegalEntityId);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { otherEntitySubject.Id }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: an inactive candidate is excluded. ResolveApproverAsync does not filter
           // the SUBJECT's own active status directly - only the resolved HOLDER's - so this is
           // excluded as a natural consequence of having no active primary assignment, no
           // department, and no manager edge (the realistic state of an offboarded employee), not
           // via a new subject-active filter. Adding such a filter to ResolveApproverAsync would be
           // out of this correction's scope and would break the equivalence test above.
    public async Task InboxScope_InactiveCandidate_IsExcluded()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var reviewerUserId = Guid.NewGuid();
        graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var inactiveSubject = graph.AddEmployee(legalEntityId, active: false);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { inactiveSubject.Id }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: an inactive reviewer (no active employee row in the legal entity)
           // receives no candidates - GetByUserAndLegalEntityAsync is active-filtered.
    public async Task InboxScope_InactiveReviewer_ReceivesNoCandidates()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var reviewerUserId = Guid.NewGuid();
        graph.AddEmployee(legalEntityId, userId: reviewerUserId, active: false);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var subject = graph.AddEmployee(legalEntityId);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: duplicate candidate ids do not produce duplicate result ids.
    public async Task InboxScope_DuplicateCandidateIds_DoNotProduceDuplicateResults()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var reviewerUserId = Guid.NewGuid();
        var reviewerPosition = graph.AddPosition(legalEntityId);
        var reviewer = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewer.Id, reviewerPosition.Id);
        graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id, subject.Id, subject.Id }),
            CancellationToken.None);

        Assert.Equal(new[] { subject.Id }, result);
    }

    [Fact] // Inbox scope: position coverage wins over department coverage for the same candidate.
    public async Task InboxScope_PositionCoverage_WinsOverDepartmentCoverage()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var department = graph.AddDepartment();

        var subject = graph.AddEmployee(legalEntityId, departmentId: department);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var reviewerUserId = Guid.NewGuid();
        var reviewerPosition = graph.AddPosition(legalEntityId);
        var reviewer = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewer.Id, reviewerPosition.Id);
        graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var otherUserId = Guid.NewGuid();
        var deptOwnerPosition = graph.AddPosition(legalEntityId);
        var deptOwner = graph.AddEmployee(legalEntityId, userId: otherUserId);
        graph.AddPrimaryAssignment(deptOwner.Id, deptOwnerPosition.Id);
        graph.AddCoverage(legalEntityId, deptOwnerPosition.Id, "Department", null, department, ownerOrder: 1);
        graph.GrantPermission(otherUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Equal(new[] { subject.Id }, result);
    }

    [Fact] // Inbox scope: department coverage wins over the reporting-line fallback for the same
           // candidate.
    public async Task InboxScope_DepartmentCoverage_WinsOverReportingLineFallback()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var department = graph.AddDepartment();

        var subject = graph.AddEmployee(legalEntityId, departmentId: department);

        var reviewerUserId = Guid.NewGuid();
        var reviewerPosition = graph.AddPosition(legalEntityId);
        var reviewer = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewer.Id, reviewerPosition.Id);
        graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Department", null, department, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var otherUserId = Guid.NewGuid();
        var managerPosition = graph.AddPosition(legalEntityId);
        var manager = graph.AddEmployee(legalEntityId, userId: otherUserId);
        graph.AddPrimaryAssignment(manager.Id, managerPosition.Id);
        graph.SetManager(subject.Id, manager.Id);
        graph.GrantPermission(otherUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Equal(new[] { subject.Id }, result);
    }

    [Fact] // Inbox scope: a pooled owner position with two active holders and no
           // ResponsibleEmployeeId resolves to no one (picks no arbitrary holder), so the candidate
           // is excluded even though the reviewer happens to be one of the holders.
    public async Task InboxScope_PooledOwnerPositionWithoutResponsibleEmployeeId_PicksNoArbitraryHolder()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var reviewerUserId = Guid.NewGuid();
        var pooledPosition = graph.AddPosition(legalEntityId);
        var reviewerHolder = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewerHolder.Id, pooledPosition.Id);
        var otherHolder = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(otherHolder.Id, pooledPosition.Id);
        graph.AddCoverage(legalEntityId, pooledPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact] // Inbox scope: a pooled owner position with a valid ResponsibleEmployeeId resolves to
           // that specific holder.
    public async Task InboxScope_PooledOwnerPositionWithValidResponsibleEmployeeId_ResolvesCorrectly()
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var reviewerUserId = Guid.NewGuid();
        var pooledPosition = graph.AddPosition(legalEntityId);
        var reviewerHolder = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewerHolder.Id, pooledPosition.Id);
        var otherHolder = graph.AddEmployee(legalEntityId);
        graph.AddPrimaryAssignment(otherHolder.Id, pooledPosition.Id);
        graph.AddCoverage(legalEntityId, pooledPosition.Id, "Position", subjectPosition.Id, null, ownerOrder: 1,
            responsibleEmployeeId: reviewerHolder.Id);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Equal(new[] { subject.Id }, result);
    }

    [Theory] // Inbox scope: N-level backup works, not hardcoded to any maximum.
    [InlineData(5)]
    [InlineData(12)]
    public async Task InboxScope_SupportsArbitraryBackupLevels_NoHardcodedMaximum(int levels)
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();

        var subject = graph.AddEmployee(legalEntityId);
        var subjectPosition = graph.AddPosition(legalEntityId);
        graph.AddPrimaryAssignment(subject.Id, subjectPosition.Id);

        var chain = BuildOwnerChain(graph, legalEntityId, subject.Id, subjectPosition.Id, levels);
        var reviewerUserId = chain[^1].Employee.UserId;
        graph.GrantPermission(reviewerUserId, AttendanceApprove); // only the last level has permission

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, new[] { subject.Id }),
            CancellationToken.None);

        Assert.Equal(new[] { subject.Id }, result);
    }

    [Theory] // Inbox scope: repository call counts stay constant as candidate count grows - proof
             // the batch refactor doesn't issue one query per candidate.
    [InlineData(50)]
    [InlineData(100)]
    public async Task InboxScope_RepositoryCallCountIsConstant_RegardlessOfCandidateCount(int candidateCount)
    {
        var graph = new EmployeeAuthorityTestGraph();
        var legalEntityId = Guid.NewGuid();
        var reviewerUserId = Guid.NewGuid();
        var reviewerPosition = graph.AddPosition(legalEntityId);
        var reviewerHolder = graph.AddEmployee(legalEntityId, userId: reviewerUserId);
        graph.AddPrimaryAssignment(reviewerHolder.Id, reviewerPosition.Id);
        graph.GrantPermission(reviewerUserId, AttendanceApprove);

        var candidateIds = new List<Guid>();
        for (var i = 0; i < candidateCount; i++)
        {
            var candidatePosition = graph.AddPosition(legalEntityId);
            var candidate = graph.AddEmployee(legalEntityId);
            graph.AddPrimaryAssignment(candidate.Id, candidatePosition.Id);
            graph.AddCoverage(legalEntityId, reviewerPosition.Id, "Position", candidatePosition.Id, null, ownerOrder: 1);
            candidateIds.Add(candidate.Id);
        }

        var resolver = graph.BuildResolver(currentUserId: reviewerUserId);

        var result = await resolver.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(legalEntityId, AttendanceApprove,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval, candidateIds),
            CancellationToken.None);

        Assert.Equal(candidateCount, result.Count);
        Assert.NotEmpty(graph.CallCounts);
        Assert.All(graph.CallCounts.Values, count => Assert.True(count <= 2,
            $"expected each batch repository method to be called at most twice regardless of candidate count, was {count}"));
    }

    /// <summary>Builds an N-level owner chain above subject: level 1 is subject's direct manager
    /// and the position-coverage primary owner (OwnerOrder 1), level 2 is that owner's manager
    /// and Backup 1 (OwnerOrder 2), and so on - mirroring how a real backup chain is both a
    /// coverage configuration and a genuine (if skip-level) reporting relationship, which the
    /// resolver's upward-only guard requires.</summary>
    private static List<(ONEVO.Domain.Features.CoreHr.Entities.Employee Employee, Guid PositionId)> BuildOwnerChain(
        EmployeeAuthorityTestGraph graph, Guid legalEntityId, Guid subjectId, Guid subjectPositionId, int levels)
    {
        var chain = new List<(ONEVO.Domain.Features.CoreHr.Entities.Employee Employee, Guid PositionId)>();
        var previousId = subjectId;

        for (var level = 1; level <= levels; level++)
        {
            var position = graph.AddPosition(legalEntityId);
            var employee = graph.AddEmployee(legalEntityId);
            graph.AddPrimaryAssignment(employee.Id, position.Id);
            graph.SetManager(previousId, employee.Id);
            graph.AddCoverage(legalEntityId, position.Id, "Position", subjectPositionId, null, ownerOrder: level);

            chain.Add((employee, position.Id));
            previousId = employee.Id;
        }

        return chain;
    }
}
