using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeApprovalActivity;

/// <summary>
/// "Approval Activity" for one employee: requests they made (or project invitations sent to them)
/// across leave, attendance and Work Management, newest first. Each source is included only if the
/// caller holds that module's permission or is viewing their own record.
/// </summary>
public sealed class GetEmployeeApprovalActivityQueryHandler(
    IEmployeeReadAccessGuard guard,
    IEmployeeRepository employees,
    ILeaveRequestRepository leave,
    IAttendanceCorrectionRepository corrections,
    IWorkAreaChangeRequestRepository workAreas,
    ILocationChangeRequestRepository locations,
    IWorkApprovalHistoryRepository workApprovals,
    ICallerIdentityResolver identity,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetEmployeeApprovalActivityQuery, Result<EmployeeApprovalActivityResponse>>
{
    public const int MaxItems = 10;
    private const int PerSourceCap = 200;

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["leave"] = "Leave request",
        ["attendance_correction"] = "Attendance correction",
        ["work_area_change"] = "Work area change",
        ["location_change"] = "Location change",
        ["task_creation"] = "Task creation",
        ["task_edit"] = "Task edit",
        ["objective_edit"] = "Objective edit",
        ["allocation_extend"] = "Allocation extension",
        ["objective_change"] = "Objective change",
        ["task_status_change"] = "Task status change",
        ["project_invitation"] = "Project invitation"
    };

    public async Task<Result<EmployeeApprovalActivityResponse>> Handle(
        GetEmployeeApprovalActivityQuery request, CancellationToken ct)
    {
        var tenantId = currentUser.TenantId;
        var employeeId = request.EmployeeId;

        var access = await guard.EnsureCanRead(tenantId, employeeId, ct);
        if (!access.IsSuccess)
            return Result<EmployeeApprovalActivityResponse>.Failure(access.Error!, access.StatusCode ?? 400);

        var period = EmployeePeriod.Resolve(request.From, request.To, clock.Today);
        if (!period.IsSuccess)
            return Result<EmployeeApprovalActivityResponse>.Failure(period.Error!, period.StatusCode ?? 400);

        var caller = await employees.GetDefaultForUserAsync(tenantId, currentUser.UserId, ct);
        var isSelf = caller is not null && caller.Id == employeeId;
        bool Has(string permission) => isSelf || currentUser.HasPermission(permission);
        var canLeave = Has("leave:read");
        var canAttendance = Has("attendance:read");
        var canWork = Has("tasks:read");
        if (!canLeave && !canAttendance && !canWork)
            return Result<EmployeeApprovalActivityResponse>.Forbidden("You do not have access to this employee's approval activity.");

        var fromUtc = new DateTimeOffset(period.Value!.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var toUtcExclusive = new DateTimeOffset(period.Value.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        bool InWindow(DateTimeOffset at) => at >= fromUtc && at < toUtcExclusive;

        var items = new List<EmployeeApprovalItem>();

        if (canLeave)
        {
            var rows = await leave.ListOwnAsync(tenantId, employeeId, new LeaveRequestListFilter(null, null, null, null), ct);
            items.AddRange(rows.Where(r => InWindow(r.Request.CreatedAt)).Select(r => new EmployeeApprovalItem(
                r.Request.Id.ToString(), "leave", Labels["leave"],
                $"{r.LeaveTypeName} · {DateOnly.FromDateTime(r.Request.StartAt.UtcDateTime):yyyy-MM-dd} → {DateOnly.FromDateTime(r.Request.EndAt.UtcDateTime):yyyy-MM-dd}",
                NormalizeStatus(r.Request.Status), r.Request.CreatedAt, r.Request.ApprovedAt, null)));
        }

        if (canAttendance)
        {
            var (correctionRows, _) = await corrections.ListMyAsync(tenantId, employeeId, null, null, null, 0, PerSourceCap, ct);
            items.AddRange(correctionRows.Where(c => InWindow(c.CreatedAt)).Select(c => new EmployeeApprovalItem(
                c.Id.ToString(), "attendance_correction", Labels["attendance_correction"],
                $"{c.WorkDate:yyyy-MM-dd} · {c.CorrectionType.Replace('_', ' ')}",
                NormalizeStatus(c.Status), c.CreatedAt, c.ReviewedAt, null)));

            var (workAreaRows, _) = await workAreas.ListMyAsync(tenantId, employeeId, null, null, null, 0, PerSourceCap, ct);
            items.AddRange(workAreaRows.Where(w => InWindow(w.RequestedAt)).Select(w => new EmployeeApprovalItem(
                w.Id.ToString(), "work_area_change", Labels["work_area_change"],
                $"{w.Date:yyyy-MM-dd} · {w.CurrentWorkModeName} → {w.RequestedWorkModeName}",
                NormalizeStatus(w.Status), w.RequestedAt, w.ReviewedAt, null)));

            var (locationRows, _) = await locations.ListMyAsync(tenantId, employeeId, null, 0, PerSourceCap, ct);
            items.AddRange(locationRows.Where(l => InWindow(l.RequestedAt)).Select(l => new EmployeeApprovalItem(
                l.Id.ToString(), "location_change", Labels["location_change"], null,
                NormalizeStatus(l.Status), l.RequestedAt, l.ReviewedAt, null)));
        }

        if (canWork)
        {
            var records = await workApprovals.ListRequestedByEmployeeAsync(tenantId, employeeId, fromUtc, toUtcExclusive, ct);
            var nameIds = records
                .Where(r => r.Kind != "objective_invitation")
                .Select(r => r.DecidedById ?? r.ApproverId)
                .Distinct()
                .ToList();
            var names = nameIds.Count == 0
                ? new Dictionary<Guid, string>()
                : (await identity.ResolveDisplayNamesByEmployeeIdAsync(tenantId, nameIds, ct)).ToDictionary(p => p.Key, p => p.Value);

            items.AddRange(records.Select(r =>
            {
                var isInvitation = r.Kind == "objective_invitation";
                var kind = isInvitation ? "project_invitation" : r.Kind;
                string? approver = isInvitation ? null : names.GetValueOrDefault(r.DecidedById ?? r.ApproverId);
                return new EmployeeApprovalItem(
                    r.Id.ToString(), kind, Labels.GetValueOrDefault(kind, "Approval"), r.SubjectTitle,
                    NormalizeStatus(r.Status), r.CreatedAt, r.DecidedAt, approver);
            }));
        }

        var ordered = items.OrderByDescending(i => i.RequestedAt).ToList();
        return Result<EmployeeApprovalActivityResponse>.Success(new EmployeeApprovalActivityResponse(
            period.Value.From,
            period.Value.To,
            ordered.Count(i => i.Status == "pending"),
            ordered.Count(i => i.Status == "approved"),
            ordered.Count(i => i.Status == "rejected"),
            ordered.Count,
            ordered.Take(MaxItems).ToList()));
    }

    private static string NormalizeStatus(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "pending" => "pending",
        "approved" or "accepted" or "applied" => "approved",
        "rejected" or "declined" => "rejected",
        _ => "cancelled"
    };
}
