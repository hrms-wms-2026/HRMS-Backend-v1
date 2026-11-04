using MediatR;
using Microsoft.Extensions.Options;
using ONEVO.Application.Features.WorkManagement.Planner;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Leadership.Services;

namespace ONEVO.Application.Features.Dashboard.Team.Queries;

/// <summary>My Team capability discovery (spec §7): every flag is a cheap existence probe, never
/// a scope expansion - the actual population is only ever materialized by the section's own
/// request (Team Status, Approvals &amp; Exceptions, Team Progress). Per spec §11, a caller with
/// no default employee record gets canViewPeopleStatus/leadsWork/hasWorkApprovals = false and
/// legalEntityId = null, but canReviewPeopleApprovals/canReviewExceptions - pure permission
/// checks - still compute normally ("My Team is then offered only through permission-based
/// approval flags").</summary>
public sealed class GetMyTeamCapabilitiesQueryHandler(
    ICurrentUser currentUser,
    IEmployeeRepository employees,
    IEmployeeAuthorityResolver authority,
    IModuleEntitlementService modules,
    IWorkLeadershipService workLeadership,
    ONEVO.Application.Common.RepositoryInterfaces.IEmployeeRepository? reportingLines = null,
    IOptions<PlannerOptions>? plannerOptions = null)
    : IRequestHandler<GetMyTeamCapabilitiesQuery, Result<MyTeamCapabilitiesResponse>>
{
    private const string AttendanceReadPermission = "attendance:read";

    private static readonly string[] WorkModuleKeys =
        ["worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints"];

    public async Task<Result<MyTeamCapabilitiesResponse>> Handle(GetMyTeamCapabilitiesQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<MyTeamCapabilitiesResponse>.Forbidden();

        var canReviewPeopleApprovals = currentUser.HasPermission("leave:approve") || currentUser.HasPermission("attendance:approve");
        var canReviewExceptions = currentUser.HasPermission(ExceptionPermissions.HrReview)
            || currentUser.HasPermission(ExceptionPermissions.HrManage)
            || currentUser.HasPermission(ExceptionPermissions.ManagerReview)
            || currentUser.HasPermission(ExceptionPermissions.View);

        var actor = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        var legalEntityId = actor?.LegalEntityId;

        var canViewPeopleStatus = false;
        var leadsWork = false;
        var hasWorkApprovals = false;
        var hasReports = false;

        // One reporting-line probe feeds both Today's Team Schedule (hasReports) and the team planner (canViewTeamPlan).
        var hasDirectReports = actor is not null && reportingLines is not null
            && (await reportingLines.ListDirectReportEmployeeIdsAsync(currentUser.TenantId, [actor.Id], ct)).Count > 0;

        if (legalEntityId is Guid activeLegalEntityId)
        {
            // The reporting line through the common repository, never the raw closure (My Team spec section 10 rule 1).
            hasReports = hasDirectReports;

            if (currentUser.HasPermission(AttendanceReadPermission))
            {
                canViewPeopleStatus = await authority.HasAnyManagedCoverageAsync(
                    new EmployeeAuthorityVisibilityRequest(
                        currentUser.UserId, activeLegalEntityId, AttendanceReadPermission,
                        IncludeSelf: false, EmployeeAuthorityPurpose.TimeTrackingRead), ct);
            }

            var activeModules = await modules.GetActiveModuleKeysForTenantAsync(currentUser.TenantId, ct);
            var hasWorkModule = WorkModuleKeys.Any(key => activeModules.Contains(key, StringComparer.OrdinalIgnoreCase));
            if (hasWorkModule)
            {
                leadsWork = await workLeadership.LeadsAnyWorkAsync(
                    currentUser.TenantId, actor!.Id, activeLegalEntityId, ct);
                hasWorkApprovals = await workLeadership.HasPendingWorkApprovalsAsync(
                    currentUser.TenantId, actor.Id, activeLegalEntityId, ct);
            }
        }

        // Team planner (2026-11-03): a cheap existence probe on the reporting line, like the flags above.
        var canViewTeamPlan = hasDirectReports && plannerOptions?.Value.Enabled == true;

        var isAvailable = canViewPeopleStatus || canReviewPeopleApprovals || canReviewExceptions || hasWorkApprovals || leadsWork
            || canViewTeamPlan;

        return Result<MyTeamCapabilitiesResponse>.Success(new MyTeamCapabilitiesResponse(
            isAvailable,
            canViewPeopleStatus,
            canReviewPeopleApprovals,
            canReviewExceptions,
            hasWorkApprovals,
            leadsWork,
            CanViewLiveActivity: false,
            hasReports,
            legalEntityId,
            canViewTeamPlan));
    }
}
