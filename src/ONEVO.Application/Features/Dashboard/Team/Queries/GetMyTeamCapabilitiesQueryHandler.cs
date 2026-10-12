using MediatR;
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
    IWorkLeadershipService workLeadership)
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

        if (legalEntityId is Guid activeLegalEntityId)
        {
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

        var isAvailable = canViewPeopleStatus || canReviewPeopleApprovals || canReviewExceptions || hasWorkApprovals || leadsWork;

        return Result<MyTeamCapabilitiesResponse>.Success(new MyTeamCapabilitiesResponse(
            isAvailable,
            canViewPeopleStatus,
            canReviewPeopleApprovals,
            canReviewExceptions,
            hasWorkApprovals,
            leadsWork,
            CanViewLiveActivity: false,
            legalEntityId));
    }
}
