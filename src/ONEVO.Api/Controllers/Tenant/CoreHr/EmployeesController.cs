using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.CoreHr.Employees;
using ONEVO.Api.Contracts.Storage;
using ONEVO.Api.Filters;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddDependent;
using ONEVO.Application.Features.CoreHr.Employee.Commands.BulkChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Employee.Commands.BulkChangeEmploymentType;
using ONEVO.Application.Features.CoreHr.Offboarding.Commands.BulkStartOffboarding;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ChangeEmployeePosition;
using ONEVO.Application.Features.CoreHr.Employee.Commands.AddEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteDependent;
using ONEVO.Application.Features.CoreHr.Employee.Commands.DeleteEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.Commands.LinkMyAvatar;
using ONEVO.Application.Features.CoreHr.Employee.Commands.ResendEmployeeInvitation;
using ONEVO.Application.Features.CoreHr.Employee.Commands.RevealEmployeeBankDetails;
using ONEVO.Application.Features.CoreHr.Employee.Commands.RevokeEmployeeInvitation;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateBankDetails;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateDependent;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmergencyContact;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdateEmployeeJobDetails;
using ONEVO.Application.Features.CoreHr.Employee.Commands.UpdatePersonalInformation;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeChecklistOverview;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeDetail;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeHistory;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeIdentity;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeOverviewSignals;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeSignalItems;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeePositionHistory;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeRecentActivity;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeUpcoming;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyPayroll;
using ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;
using ONEVO.Application.Features.CoreHr.Employee.Queries.ListEmployees;
using ONEVO.Application.Features.Leave.Balance.Queries.GetEmployeeTimeOff;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeOverview.GetEmployeeActivityOverview;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDiscipline;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceOverview;
using ONEVO.Application.Features.TimeAttendance.Queries.EmployeeOverview.GetEmployeeAttendanceDays;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeDelivery;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkOverview;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.GetEmployeeWorkTasks;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeActivityByHour;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeAppUsage;
using ONEVO.Application.Features.Monitoring.ActivityMonitoring.Queries.EmployeeWorkActivity.GetEmployeeWorkPattern;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeDeliveryTrend;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeNeedsAttention;
using ONEVO.Application.Features.WorkManagement.EmployeeOverview.Queries.WorkActivity.GetEmployeeRecentTasks;
using ONEVO.Application.Features.WorkManagement.EmployeeWorkGraph.Queries.GetEmployeeWorkGraph;

namespace ONEVO.Api.Controllers.Tenant.CoreHr;

[ApiController]
[Route("api/v1/employees")]
[Authorize(Policy = "TenantPolicy")]
public class EmployeesController : ControllerBase
{
    private readonly IMediator _mediator;

    public EmployeesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>List employees visible to the caller. Tenant-scoped, paginated, and filtered
    /// by the caller's management-coverage-derived visibility scope unless they hold
    /// org:manage.</summary>
    [HttpGet]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] Guid? departmentId = null,
        [FromQuery] Guid? legalEntityId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] Guid? positionId = null,
        [FromQuery(Name = "employmentType")] string[]? employmentTypes = null,
        [FromQuery] Guid? managerId = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
         var result = await _mediator.Send(
            new ListEmployeesQuery(
                search,
                departmentId,
                legalEntityId,
                page,
                pageSize,
                positionId,
                employmentTypes,
                managerId,
                sortBy,
                string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase),
                activeOnly), ct);


        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get a single employee by ID. Returns 404 if the employee does not exist in the
    /// caller's tenant, 403 if it exists but is outside the caller's visibility scope.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Name and avatar only - universal display identity for any employee in this
    /// tenant, deliberately gated on nothing beyond authentication (no employees:read, no
    /// visibility-scope check). For showing "who owns/is assigned to this" elsewhere in the
    /// app; the full HR record stays behind GetById/GetDetail above.</summary>
    [HttpGet("{id:guid}/identity")]
    public async Task<IActionResult> GetIdentity(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeIdentityQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Full section-by-section detail read for one employee. Payroll is included only
    /// when the caller holds employees:read:sensitive - omitted (not a 403) otherwise.</summary>
    [HttpGet("{id:guid}/detail")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetDetail(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeDetailQuery(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Reveal an employee's complete primary bank details. The normal detail response
    /// remains masked; every successful reveal is recorded in the append-only audit log.</summary>
    [HttpPost("{id:guid}/bank-details/reveal")]
    [RequirePermission("employees:read:sensitive")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevealBankDetails(Guid id, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        var result = await _mediator.Send(new RevealEmployeeBankDetailsCommand(id), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Job Journey: primary-employment position history for one employee, oldest first.
    /// Planned assignments are excluded. ApprovedByName is set only when an approved access-grant
    /// request exists for that assignment.</summary>
    [HttpGet("{id:guid}/position-history")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetPositionHistory(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeePositionHistoryQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work Network graph: the employee's projects, modules (objectives) and open tasks
    /// as nodes/links. Coverage-scoped like the detail read; see GetEmployeeWorkGraphQueryHandler.</summary>
    [HttpGet("{id:guid}/work-graph")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetWorkGraph(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeWorkGraphQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview attendance card: present/late/missing-clock-out/leave counts and the
    /// per-day strip for one employee over from..to (default: current month).</summary>
    [HttpGet("{id:guid}/overview/attendance")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewAttendance(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeAttendanceOverviewQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview attendance card day list: every day of from..to up to today (newest first) with
    /// status, clock-in/out, worked and break minutes, late/early minutes. Fetched only when the card is opened.</summary>
    [HttpGet("{id:guid}/overview/attendance/days")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewAttendanceDays(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeAttendanceDaysQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview attendance-discipline card: late clock-ins, early clock-outs, missing
    /// clock-outs, over-break days/minutes and (when location tracking is on) location alerts.</summary>
    [HttpGet("{id:guid}/overview/attendance-discipline")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewAttendanceDiscipline(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? compare = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeAttendanceDisciplineQuery(id, from, to, compare), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview time-off card: this year's leave balances (hours) plus the next
    /// approved leave. Year-based - it does not follow the month period.</summary>
    [HttpGet("{id:guid}/overview/time-off")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewTimeOff(
        Guid id, [FromQuery] int? year = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeTimeOffQuery(id, year), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview work card: assigned/completed/in-progress/overdue task counts and
    /// completion + on-time rates for one employee over from..to (default: current month).</summary>
    [HttpGet("{id:guid}/overview/work")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetOverviewWork(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeWorkOverviewQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview work card task list: every task behind the card's counts for from..to (default:
    /// current month) with status, priority, project and module, grouped overdue / not started /
    /// in progress / completed and ordered by priority. Fetched only when the card is opened.</summary>
    [HttpGet("{id:guid}/overview/work/tasks")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetOverviewWorkTasks(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeWorkTasksQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview delivery card: tasks completed, on-time completion and story points; with
    /// compare=previous also the previous period's figures.</summary>
    [HttpGet("{id:guid}/overview/delivery")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetOverviewDelivery(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? compare = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeDeliveryQuery(id, from, to, compare), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview activity panel: active / idle / meeting minutes summed from the persisted
    /// daily summaries; with compare=previous also the previous period. Reports
    /// activityMonitoringEnabled=false (and zeros) when monitoring is off for the employee.</summary>
    [HttpGet("{id:guid}/overview/activity")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewActivity(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] string? compare = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeActivityOverviewQuery(id, from, to, compare), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview approval activity: requests the employee made in from..to across leave,
    /// attendance and Work Management (newest 10 plus pending/approved/rejected counts). Each source
    /// is included only when the caller may read it or is viewing their own record.</summary>
    [HttpGet("{id:guid}/overview/approvals")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewApprovals(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeApprovalActivityQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work & Activity Needs attention card: open tasks overdue or due within 3 days (top 5 + total).</summary>
    [HttpGet("{id:guid}/work-activity/needs-attention")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetWorkActivityNeedsAttention(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeNeedsAttentionQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work & Activity Recent tasks card: the 5 most recently changed assigned tasks.</summary>
    [HttpGet("{id:guid}/work-activity/recent-tasks")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetWorkActivityRecentTasks(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeRecentTasksQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work & Activity delivery chart: completed tasks per month for the 6 months ending at `to`'s month.</summary>
    [HttpGet("{id:guid}/work-activity/delivery-trend")]
    [RequirePermission("employees:read")]
    [RequireAnyModule("worksync_foundation", "projects", "objectives_milestones", "tasks", "boards", "planning_sprints")]
    public async Task<IActionResult> GetWorkActivityDeliveryTrend(Guid id, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeDeliveryTrendQuery(id, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work & Activity hourly chart: active minutes per local hour of day over the period (max 31 days).</summary>
    [HttpGet("{id:guid}/work-activity/activity-by-hour")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetWorkActivityByHour(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeActivityByHourQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work & Activity Application usage card: top 5 apps with minutes, sessions and last use (max 31 days).</summary>
    [HttpGet("{id:guid}/work-activity/app-usage")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetWorkActivityAppUsage(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeAppUsageQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Work & Activity Work pattern card: focus blocks, top app and longest idle period (max 31 days).</summary>
    [HttpGet("{id:guid}/work-activity/work-pattern")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetWorkActivityWorkPattern(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeWorkPatternQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview signals: every active violation for the employee in from..to, ordered by
    /// importance rank. Drives the KPI strip and Needs Attention card. Module-gated or
    /// out-of-scope sources are omitted inside the handler, not rejected here.</summary>
    [HttpGet("{id:guid}/overview/signals")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewSignals(
        Guid id, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeOverviewSignalsQuery(id, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Needs Attention drill-down: the days / alerts / cases / tasks / approvals behind one
    /// overview signal (at most 50, newest first; total is the full count). 404 for an unknown key,
    /// 403 when that signal's source is hidden from the caller.</summary>
    [HttpGet("{id:guid}/overview/signals/{key}/items")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewSignalItems(
        Guid id, string key, [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeSignalItemsQuery(id, key, from, to), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview checklists card: onboarding/offboarding task progress grouped by category.
    /// Lifetime view (not period-aware).</summary>
    [HttpGet("{id:guid}/overview/checklists")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewChecklists(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeChecklistOverviewQuery(id), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview employee history: joined, probation, position and reporting-manager changes,
    /// approved leave, completed checklist tasks and termination - newest first. Lifetime view.</summary>
    [HttpGet("{id:guid}/overview/history")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewHistory(Guid id, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeHistoryQuery(id, limit), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview upcoming items: calendar events (private ones excluded), approved leave and
    /// release reminders in the next N days (default 14, max 60), earliest first. `limit` (default 10, max 50)
    /// caps the items returned; `total` in the response counts the whole window. Not period-aware.</summary>
    [HttpGet("{id:guid}/overview/upcoming")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewUpcoming(Guid id, [FromQuery] int days = 14, [FromQuery] int limit = 10, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeUpcomingQuery(id, days, limit), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Overview recent activity: what the employee did (attendance, leave, Work Management),
    /// newest first; pass the previous response's nextBefore as `before` for older items. Partial by
    /// design - see GetEmployeeRecentActivityQueryHandler.</summary>
    [HttpGet("{id:guid}/overview/recent-activity")]
    [RequirePermission("employees:read")]
    public async Task<IActionResult> GetOverviewRecentActivity(
        Guid id, [FromQuery] DateTimeOffset? before = null, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEmployeeRecentActivityQuery(id, before, limit), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Reassign an employee's primary position. Minimal capacity-checked reassignment -
    /// not an approval-routed workflow. See ChangeEmployeePositionCommandHandler.</summary>
    [HttpPost("{id:guid}/change-position")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> ChangePosition(
        Guid id, [FromBody] ChangePositionRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new ChangeEmployeePositionCommand(id, request.PositionId, request.EffectiveFrom, request.ChangeReason, request.ReportsToEmployeeId), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update an existing employee's Employee Number, Employment Type, and Work Mode -
    /// the three Job & Organizational Details fields with no other dedicated update flow.
    /// Position/reporting-manager changes stay on change-position.</summary>
    [HttpPut("{id:guid}/job-details")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> UpdateJobDetails(
        Guid id, [FromBody] UpdateEmployeeJobDetailsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateEmployeeJobDetailsCommand(id, request.EmployeeNumber, request.EmploymentTypeCode, request.WorkModeId), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Promote/transfer many employees into one position. Each employee goes through the
    /// single change-position rules; the response reports the outcome per employee.</summary>
    [HttpPost("bulk/change-position")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> BulkChangePosition(
        [FromBody] BulkChangePositionRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new BulkChangeEmployeePositionCommand(
            request.EmployeeIds, request.PositionId, request.EffectiveFrom, request.ChangeReason, request.ReportsToEmployeeId), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("bulk/employment-type")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> BulkChangeEmploymentType(
        [FromBody] BulkChangeEmploymentTypeRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new BulkChangeEmploymentTypeCommand(request.EmployeeIds, request.EmploymentTypeCode), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    [HttpPost("bulk/offboarding/start")]
    [RequirePermission("employees:offboard")]
    public async Task<IActionResult> BulkStartOffboarding(
        [FromBody] BulkStartOffboardingRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new BulkStartOffboardingCommand(
            request.EmployeeIds, request.Reason, request.LastWorkingDate, request.KnowledgeRiskLevel, request.RehireEligibility, request.Notes), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Resend an employee onboarding invitation. Only succeeds when the employee's
    /// current invitation has expired without being accepted - see
    /// ResendEmployeeInvitationCommandHandler for the exact guard.</summary>
    [HttpPost("{id:guid}/resend-invitation")]
    [RequirePermission("invitations:manage")]
    public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ResendEmployeeInvitationCommand(id), ct);

        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Revoke an employee's current onboarding invitation and free its reserved seat.
    /// Unlike resend, this works on a still-pending invitation, not only an expired one.</summary>
    [HttpPost("{id:guid}/revoke-invitation")]
    [RequirePermission("invitations:manage")]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RevokeEmployeeInvitationCommand(id), ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Composite read of the caller's own profile: personal info, job info (read-only),
    /// emergency contacts, dependents, masked payroll, and security status. Self-service only -
    /// no permission code required, matches profile-management.md's "authenticated self-service".</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyProfileQuery(), ct);
        return result.IsSuccess
            ? Ok(result.Value)
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Links a pending employee_avatar upload as the caller's current avatar.</summary>
    [HttpPut("me/avatar")]
    public async Task<IActionResult> LinkMyAvatar([FromBody] LinkFileRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new LinkMyAvatarCommand(request.FileId), ct);
        return result.IsSuccess
            ? Ok(new { avatarFileId = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update the caller's own Personal Information. Optimistic concurrency: Version must
    /// match the xmin token returned by GetMyProfile, or this returns 409.</summary>
    [HttpPut("me/personal-information")]
    public async Task<IActionResult> UpdateMyPersonalInformation(
        [FromBody] UpdatePersonalInformationRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdatePersonalInformationCommand(
                request.FirstName, request.LastName, request.Phone, request.DateOfBirth,
                request.Gender, request.NationalityId, request.DisplayTimezone,
                request.Addresses.Select(a => new UpdateAddressInput(a.AddressType, a.AddressJson, a.IsPrimary)).ToList(),
                request.Version),
            ct);

        return result.IsSuccess
            ? NoContent()
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Add an emergency contact for the caller's own profile.</summary>
    [HttpPost("me/emergency-contacts")]
    public async Task<IActionResult> AddMyEmergencyContact([FromBody] UpsertEmergencyContactRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new AddEmergencyContactCommand(request.Name, request.Relationship, request.Phone, request.Email, request.IsPrimary), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMyProfile), null, new { id = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update one of the caller's own emergency contacts.</summary>
    [HttpPut("me/emergency-contacts/{contactId:guid}")]
    public async Task<IActionResult> UpdateMyEmergencyContact(
        Guid contactId, [FromBody] UpsertEmergencyContactRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateEmergencyContactCommand(contactId, request.Name, request.Relationship, request.Phone, request.Email, request.IsPrimary), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Remove one of the caller's own emergency contacts.</summary>
    [HttpDelete("me/emergency-contacts/{contactId:guid}")]
    public async Task<IActionResult> DeleteMyEmergencyContact(Guid contactId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeleteEmergencyContactCommand(contactId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Add a dependent for the caller's own profile.</summary>
    [HttpPost("me/dependents")]
    public async Task<IActionResult> AddMyDependent([FromBody] UpsertDependentRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new AddDependentCommand(request.Name, request.Relationship, request.DateOfBirth, request.IsEmergencyContact, request.Phone), ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetMyProfile), null, new { id = result.Value })
            : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update one of the caller's own dependents.</summary>
    [HttpPut("me/dependents/{dependentId:guid}")]
    public async Task<IActionResult> UpdateMyDependent(
        Guid dependentId, [FromBody] UpsertDependentRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateDependentCommand(dependentId, request.Name, request.Relationship, request.DateOfBirth, request.IsEmergencyContact, request.Phone), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Remove one of the caller's own dependents.</summary>
    [HttpDelete("me/dependents/{dependentId:guid}")]
    public async Task<IActionResult> DeleteMyDependent(Guid dependentId, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeleteDependentCommand(dependentId), ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Get the caller's own masked payroll/bank details.</summary>
    [HttpGet("me/payroll")]
    public async Task<IActionResult> GetMyPayroll(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMyPayrollQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }

    /// <summary>Update the caller's own bank details. Requires employees:write even for the
    /// caller's own record - bank-detail edits are HR-mediated to prevent unauthorized
    /// payroll-redirection (see design spec §6).</summary>
    [HttpPut("me/payroll")]
    [RequirePermission("employees:write")]
    public async Task<IActionResult> UpdateMyPayroll([FromBody] UpdateBankDetailsRequest request, CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new UpdateBankDetailsCommand(
                request.BankName, request.BranchName, request.AccountHolderName,
                request.AccountNumber, request.AccountType, request.RoutingNumber),
            ct);
        return result.IsSuccess ? NoContent() : Problem(result.Error, statusCode: result.StatusCode ?? 400);
    }
}
