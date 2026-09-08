using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries.WorkAreaChangeRequests;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Services;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using MediatR;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests;

public sealed class WorkAreaChangeRequestWorkflow(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    CoreEmployeeRepository employees,
    ISessionRepository sessions,
    ILegalEntityRepository legalEntities,
    IWorkAreaChangeRequestRepository requests,
    IAttendanceReadRepository attendance,
    IExpectedWorkAreaResolver expectedAreas,
    IEmployeeAuthorityResolver authority,
    IPositionRepository positions,
    INotificationDispatcher notifications,
    IUnitOfWork unitOfWork)
{
    private const string ApprovalPermission = "attendance:approve";
    private const string RelatedType = "work_area_change_request";
    private const string CreatedTemplate = "work_area_change_request_created";
    private const string DecidedTemplate = "work_area_change_request_decided";
    private const string CancelledTemplate = "work_area_change_request_cancelled";

    public async Task<Result<WorkAreaChangeRequestPreviewResponse>> PreviewAsync(
        PreviewWorkAreaChangeRequestCommand command, CancellationToken ct)
    {
        var prepared = await PrepareAsync(command.Date, command.RequestedWorkArea, command.Reason, ct);
        if (!prepared.IsSuccess)
            return Result<WorkAreaChangeRequestPreviewResponse>.Failure(prepared.Error!, prepared.StatusCode ?? 400);

        var value = prepared.Value!;
        return Result<WorkAreaChangeRequestPreviewResponse>.Success(new(
            value.Date,
            value.Expected.Timezone,
            value.Expected.WorkArea,
            value.RequestedWorkArea,
            value.Reason,
            value.Receiver));
    }

    public async Task<Result<WorkAreaChangeRequestResponse>> CreateAsync(
        CreateWorkAreaChangeRequestCommand command, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<WorkAreaChangeRequestResponse>.Forbidden("Authentication is required.");

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
            {
                var prepared = await PrepareAsync(command.Date, command.RequestedWorkArea, command.Reason, transactionCt);
                if (!prepared.IsSuccess)
                    return Result<WorkAreaChangeRequestResponse>.Failure(prepared.Error!, prepared.StatusCode ?? 400);

                var value = prepared.Value!;
                var request = new WorkAreaChangeRequest
                {
                    Id = Guid.NewGuid(),
                    TenantId = currentUser.TenantId,
                    EmployeeId = value.Employee.Id,
                    LegalEntityId = value.LegalEntity.Id,
                    Date = value.Date,
                    CurrentExpectedWorkArea = value.Expected.WorkArea,
                    RequestedWorkArea = value.RequestedWorkArea,
                    Reason = value.Reason,
                    Status = WorkAreaChangeRequest.StatusPending,
                    RequestedAt = dateTime.UtcNow
                };
                await requests.AddAsync(request, transactionCt);
                await notifications.SendTemplatedAsync(
                    currentUser.TenantId,
                    value.Route!.ApproverUserId,
                    CreatedTemplate,
                    new Dictionary<string, string>
                    {
                        ["employeeName"] = DisplayName(value.Employee),
                        ["date"] = value.Date.ToString("yyyy-MM-dd"),
                        ["requestedWorkArea"] = value.RequestedWorkArea
                    },
                    RelatedType,
                    request.Id,
                    transactionCt);
                await requests.SaveChangesAsync(transactionCt);

                return Result<WorkAreaChangeRequestResponse>.Success(
                    ToResponse(request, value.Employee, value.Expected.Timezone, value.Receiver));
            }, ct);
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<WorkAreaChangeRequestResponse>.Conflict(
                "An active work-area change request already exists for this employee and date.");
        }
        catch (ConcurrencyConflictException)
        {
            return Result<WorkAreaChangeRequestResponse>.Conflict(
                "This work-area change request was changed by another request. Please refresh and try again.");
        }
    }

    public Task<Result<WorkAreaChangeRequestResponse>> ApproveAsync(
        ApproveWorkAreaChangeRequestCommand command, CancellationToken ct)
        => DecideAsync(command.Id, WorkAreaChangeRequest.StatusApproved, command.ReviewComment, ct);

    public Task<Result<WorkAreaChangeRequestResponse>> RejectAsync(
        RejectWorkAreaChangeRequestCommand command, CancellationToken ct)
        => DecideAsync(command.Id, WorkAreaChangeRequest.StatusRejected, command.ReviewComment, ct);

    public async Task<Result<WorkAreaChangeRequestResponse>> CancelAsync(
        CancelWorkAreaChangeRequestCommand command, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<WorkAreaChangeRequestResponse>.Forbidden("Authentication is required.");

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
            {
                var request = await requests.GetTrackedByIdAsync(currentUser.TenantId, command.Id, transactionCt);
                if (request is null)
                    return Result<WorkAreaChangeRequestResponse>.NotFound("Work-area change request was not found.");

                var context = await ResolveEmployeeContextAsync(transactionCt);
                if (!context.IsSuccess)
                    return Result<WorkAreaChangeRequestResponse>.Failure(context.Error!, context.StatusCode ?? 400);
                if (context.Value!.Employee.Id != request.EmployeeId)
                    return Result<WorkAreaChangeRequestResponse>.Forbidden("Only the requester can cancel this request.");
                if (request.Status != WorkAreaChangeRequest.StatusPending)
                    return Result<WorkAreaChangeRequestResponse>.Conflict("Only a pending work-area change request can be cancelled.");

                var route = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                    request.EmployeeId, request.LegalEntityId, ApprovalPermission,
                    EmployeeAuthorityPurpose.WorkAreaChangeApproval), transactionCt);
                if (route.IsSuccess && route.Value is not null)
                {
                    await notifications.SendTemplatedAsync(
                        currentUser.TenantId,
                        route.Value.ApproverUserId,
                        CancelledTemplate,
                        new Dictionary<string, string>
                        {
                            ["employeeName"] = DisplayName(context.Value.Employee),
                            ["date"] = request.Date.ToString("yyyy-MM-dd")
                        },
                        RelatedType,
                        request.Id,
                        transactionCt);
                }

                request.Status = WorkAreaChangeRequest.StatusCancelled;
                request.ReviewedById = currentUser.UserId;
                request.ReviewedAt = dateTime.UtcNow;
                await requests.SaveChangesAsync(transactionCt);

                var legalEntity = await legalEntities.GetByIdForTenantAsync(
                    currentUser.TenantId, request.LegalEntityId, transactionCt);
                var expected = await expectedAreas.ResolveAsync(
                    context.Value.Employee, legalEntity ?? context.Value.LegalEntity, request.Date, transactionCt);
                var timezone = expected.IsSuccess ? expected.Value!.Timezone : context.Value.LegalEntity.Timezone ?? "UTC";
                return Result<WorkAreaChangeRequestResponse>.Success(
                    ToResponse(request, context.Value.Employee, timezone));
            }, ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<WorkAreaChangeRequestResponse>.Conflict(
                "This work-area change request was changed by another request. Please refresh and try again.");
        }
    }

    public async Task<Result<PagedResult<WorkAreaChangeRequestResponse>>> ListMyAsync(
        ListMyWorkAreaChangeRequestsQuery query, CancellationToken ct)
    {
        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<PagedResult<WorkAreaChangeRequestResponse>>.Failure(context.Error!, context.StatusCode ?? 400);

        var pageNumber = query.Paging.PageNumber < 1 ? 1 : query.Paging.PageNumber;
        var pageSize = query.Paging.PageSize < 1 ? 20 : Math.Min(query.Paging.PageSize, 100);
        var (rows, totalCount) = await requests.ListMyAsync(
            currentUser.TenantId, context.Value!.Employee.Id, query.From, query.To,
            query.Status, (pageNumber - 1) * pageSize, pageSize, ct);
        var items = rows.Select(row => ToResponse(row, context.Value.Employee,
            ResolveTimezone(context.Value.LegalEntity, row.Date))).ToArray();
        return Result<PagedResult<WorkAreaChangeRequestResponse>>.Success(
            new PagedResult<WorkAreaChangeRequestResponse>(items, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<PagedResult<WorkAreaChangeRequestResponse>>> ListApprovalsAsync(
        ListWorkAreaChangeRequestApprovalsQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<PagedResult<WorkAreaChangeRequestResponse>>.Forbidden("Authentication is required.");
        if (!currentUser.HasPermission(ApprovalPermission))
            return Result<PagedResult<WorkAreaChangeRequestResponse>>.Forbidden(
                "You do not have permission to approve work-area changes.");

        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<PagedResult<WorkAreaChangeRequestResponse>>.Failure(context.Error!, context.StatusCode ?? 400);

        var value = context.Value!;
        var candidateEmployeeIds = await requests.ListPendingEmployeeIdsAsync(
            currentUser.TenantId, value.LegalEntity.Id, query.From, query.To, ct);
        var eligibleEmployeeIds = await authority.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(
                value.LegalEntity.Id,
                ApprovalPermission,
                EmployeeAuthorityPurpose.WorkAreaChangeApproval,
                candidateEmployeeIds), ct);
        var pageNumber = query.Paging.PageNumber < 1 ? 1 : query.Paging.PageNumber;
        var pageSize = query.Paging.PageSize < 1 ? 20 : Math.Min(query.Paging.PageSize, 100);
        var (rows, totalCount) = await requests.ListApprovalInboxAsync(
            currentUser.TenantId, value.LegalEntity.Id, eligibleEmployeeIds,
            query.From, query.To, (pageNumber - 1) * pageSize, pageSize, ct);
        var identities = await GetIdentityMapAsync(value.LegalEntity.Id, rows, ct);
        var items = rows.Select(row =>
        {
            identities.TryGetValue(row.EmployeeId, out var requester);
            return ToResponse(row, null, ResolveTimezone(value.LegalEntity, row.Date), null, requester?.DisplayName);
        }).ToArray();
        return Result<PagedResult<WorkAreaChangeRequestResponse>>.Success(
            new PagedResult<WorkAreaChangeRequestResponse>(items, pageNumber, pageSize, totalCount));
    }

    private async Task<Result<WorkAreaChangeRequestResponse>> DecideAsync(
        Guid id, string decision, string? reviewComment, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<WorkAreaChangeRequestResponse>.Forbidden("Authentication is required.");
        if (!currentUser.HasPermission(ApprovalPermission))
            return Result<WorkAreaChangeRequestResponse>.Forbidden(
                "You do not have permission to approve work-area changes.");
        if (decision == WorkAreaChangeRequest.StatusRejected && string.IsNullOrWhiteSpace(reviewComment))
            return Result<WorkAreaChangeRequestResponse>.Failure("A review comment is required when rejecting a request.");

        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
            {
                var request = await requests.GetTrackedByIdAsync(currentUser.TenantId, id, transactionCt);
                if (request is null)
                    return Result<WorkAreaChangeRequestResponse>.NotFound("Work-area change request was not found.");
                if (request.Status != WorkAreaChangeRequest.StatusPending)
                    return Result<WorkAreaChangeRequestResponse>.Conflict("Only a pending work-area change request can be reviewed.");

                var route = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                    request.EmployeeId, request.LegalEntityId, ApprovalPermission,
                    EmployeeAuthorityPurpose.WorkAreaChangeApproval), transactionCt);
                if (!route.IsSuccess || route.Value is null)
                    return Result<WorkAreaChangeRequestResponse>.Conflict(
                        "No eligible work-area approver is configured for this employee.");
                if (route.Value.ApproverUserId != currentUser.UserId)
                    return Result<WorkAreaChangeRequestResponse>.Forbidden(
                        "You are not an eligible approver for this request.");

                var employee = await employees.GetByIdAsync(currentUser.TenantId, request.EmployeeId, transactionCt);
                if (employee is null)
                    return Result<WorkAreaChangeRequestResponse>.NotFound("The request employee was not found.");
                var legalEntity = await legalEntities.GetByIdForTenantAsync(
                    currentUser.TenantId, request.LegalEntityId, transactionCt);
                if (legalEntity is null)
                    return Result<WorkAreaChangeRequestResponse>.NotFound("The request company was not found.");
                var expected = await expectedAreas.ResolveAsync(employee, legalEntity, request.Date, transactionCt);
                if (!expected.IsSuccess || expected.Value is null)
                    return Result<WorkAreaChangeRequestResponse>.Conflict(expected.Error ?? "The expected work area could not be resolved.");

                request.Status = decision;
                request.ReviewedById = currentUser.UserId;
                request.ReviewedAt = dateTime.UtcNow;
                request.ReviewComment = string.IsNullOrWhiteSpace(reviewComment) ? null : reviewComment.Trim();

                if (decision == WorkAreaChangeRequest.StatusApproved)
                {
                    // The request may be approved after the employee already clocked in for that
                    // date (requests are permitted for today/future dates). Today and history must
                    // not permanently disagree, so the existing attendance snapshot for that exact
                    // date is synchronized to the approved area in the same transaction. Only the
                    // planned ExpectedWorkArea changes - actual clock times, source, and breaks are
                    // untouched.
                    var existingRecord = await attendance.GetTrackedRecordAsync(
                        request.TenantId, request.EmployeeId, request.Date, transactionCt);
                    if (existingRecord is not null)
                    {
                        existingRecord.ExpectedWorkArea = request.RequestedWorkArea;
                        existingRecord.UpdatedAt = dateTime.UtcNow;
                    }
                }

                await notifications.SendTemplatedAsync(
                    currentUser.TenantId,
                    employee.UserId,
                    DecidedTemplate,
                    new Dictionary<string, string>
                    {
                        ["decision"] = decision,
                        ["date"] = request.Date.ToString("yyyy-MM-dd"),
                        ["requestedWorkArea"] = request.RequestedWorkArea,
                        ["reviewComment"] = request.ReviewComment ?? string.Empty
                    },
                    RelatedType,
                    request.Id,
                    transactionCt);
                await requests.SaveChangesAsync(transactionCt);
                var receiver = new WorkAreaChangeApproverResponse(
                    route.Value.ApproverEmployeeId,
                    route.Value.ApproverUserId,
                    "Approver",
                    null);
                return Result<WorkAreaChangeRequestResponse>.Success(
                    ToResponse(request, employee, expected.Value!.Timezone, receiver));
            }, ct);
        }
        catch (ConcurrencyConflictException)
        {
            return Result<WorkAreaChangeRequestResponse>.Conflict(
                "This work-area change request was changed by another request. Please refresh and try again.");
        }
    }

    private async Task<Result<PreparedRequest>> PrepareAsync(
        DateOnly date, string requestedWorkArea, string reason, CancellationToken ct)
    {
        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<PreparedRequest>.Failure(context.Error!, context.StatusCode ?? 400);

        var value = context.Value!;
        var expected = await expectedAreas.ResolveAsync(value.Employee, value.LegalEntity, date, ct);
        if (!expected.IsSuccess || expected.Value is null)
            return Result<PreparedRequest>.Failure(expected.Error ?? "The expected work area could not be resolved.", expected.StatusCode ?? 409);

        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(dateTime.UtcNow, expected.Value.TimezoneZone()).DateTime);
        if (date < localToday)
            return Result<PreparedRequest>.Conflict("A work-area change can only be requested for today or a future date.");

        var requested = requestedWorkArea?.Trim().ToLowerInvariant();
        if (requested is not (WorkAreaChangeRequest.WorkAreaOnsite or WorkAreaChangeRequest.WorkAreaRemote))
            return Result<PreparedRequest>.Conflict("Requested work area must be onsite or remote.");
        if (expected.Value.WorkArea == WorkAreaChangeRequest.WorkAreaField)
            return Result<PreparedRequest>.Conflict("Field work-area changes are not supported in this product flow.");
        if (expected.Value.WorkArea == WorkAreaChangeRequest.WorkAreaEither
            || expected.Value.WorkArea == requested)
            return Result<PreparedRequest>.Conflict(
                "The selected work area is already planned for this date.");
        if (string.IsNullOrWhiteSpace(reason))
            return Result<PreparedRequest>.Failure("A reason is required.");
        if (await requests.HasActiveForDateAsync(currentUser.TenantId, value.Employee.Id, date, ct))
            return Result<PreparedRequest>.Conflict(
                "An active work-area change request already exists for this employee and date.");

        var routeResult = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            value.Employee.Id, value.LegalEntity.Id, ApprovalPermission,
            EmployeeAuthorityPurpose.WorkAreaChangeApproval), ct);
        if (!routeResult.IsSuccess || routeResult.Value is null)
            return Result<PreparedRequest>.Conflict(
                "No eligible work-area approver is configured for this employee.");
        var route = routeResult.Value;
        var approverEmployee = await employees.GetByIdAsync(currentUser.TenantId, route.ApproverEmployeeId, ct);
        var position = await positions.GetByIdAsync(currentUser.TenantId, route.ApproverPositionId, ct);
        var receiver = new WorkAreaChangeApproverResponse(
            route.ApproverEmployeeId,
            route.ApproverUserId,
            approverEmployee is null ? "Approver" : DisplayName(approverEmployee),
            position?.Name);
        return Result<PreparedRequest>.Success(new(
            value.Employee, value.LegalEntity, expected.Value, date,
            requested!, reason.Trim(), route, receiver));
    }

    private async Task<Result<EmployeeContext>> ResolveEmployeeContextAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<EmployeeContext>.Forbidden("Authentication is required.");
        if (currentUser.TenantId == Guid.Empty)
            return Result<EmployeeContext>.Forbidden("Tenant context is missing.");

        var employee = await ResolveActiveEmployeeAsync(ct);
        if (employee?.LegalEntityId is null)
            return Result<EmployeeContext>.NotFound("Current employee record was not found.");
        var legalEntity = await legalEntities.GetByIdForTenantAsync(
            currentUser.TenantId, employee.LegalEntityId.Value, ct);
        return legalEntity is null
            ? Result<EmployeeContext>.NotFound("Company was not found.")
            : Result<EmployeeContext>.Success(new EmployeeContext(employee, legalEntity));
    }

    /// <summary>Prefers the legal entity the user actually switched to via SwitchActiveCompanyCommand
    /// (Session.ActiveEmployeeId, the same field that command writes) over GetDefaultForUserAsync's
    /// login-time "most recent primary employment" heuristic - the two can disagree for a user with
    /// employee rows in more than one legal entity. Falls back to the heuristic only when the current
    /// session has no active employee set (e.g. a session predating the ActiveEmployeeId migration) or
    /// no session id is present at all, matching pre-existing behavior for that edge case exactly.</summary>
    private async Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> ResolveActiveEmployeeAsync(CancellationToken ct)
    {
        if (currentUser.SessionId is Guid sessionId)
        {
            var session = await sessions.GetByIdAsync(sessionId, ct);
            if (session is not null
                && !session.IsRevoked
                && session.TenantId == currentUser.TenantId
                && session.UserId == currentUser.UserId
                && session.ActiveEmployeeId is Guid activeEmployeeId)
            {
                var activeEmployee = await employees.GetByIdAsync(currentUser.TenantId, activeEmployeeId, ct);
                if (activeEmployee is not null && activeEmployee.UserId == currentUser.UserId)
                    return activeEmployee;
            }
        }

        return await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
    }

    private async Task<Dictionary<Guid, EmployeeIdentity>> GetIdentityMapAsync(
        Guid legalEntityId, IReadOnlyList<WorkAreaChangeRequest> rows, CancellationToken ct)
    {
        var identities = await attendance.ListEmployeeIdentitiesAsync(
            currentUser.TenantId,
            legalEntityId,
            rows.Select(x => x.EmployeeId).Distinct().ToArray(),
            ct);
        return identities.ToDictionary(
            x => x.Key,
            x => new EmployeeIdentity(x.Value.DisplayName));
    }

    private static string DisplayName(ONEVO.Domain.Features.CoreHr.Entities.Employee employee)
        => $"{employee.FirstName} {employee.LastName}".Trim();

    private static WorkAreaChangeRequestResponse ToResponse(
        WorkAreaChangeRequest request,
        ONEVO.Domain.Features.CoreHr.Entities.Employee? employee,
        string timezone,
        WorkAreaChangeApproverResponse? receiver = null,
        string? requesterDisplayName = null)
        => new(
            request.Id,
            request.EmployeeId,
            request.LegalEntityId,
            requesterDisplayName ?? (employee is null ? "Employee" : DisplayName(employee)),
            timezone,
            request.Date,
            request.CurrentExpectedWorkArea,
            request.RequestedWorkArea,
            request.Reason,
            request.Status,
            request.RequestedAt,
            request.ReviewedById,
            null,
            request.ReviewedAt,
            request.ReviewComment,
            receiver);

    private string ResolveTimezone(
        ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity legalEntity, DateOnly date)
        => AttendanceScheduleResolver.ResolveForDate(legalEntity, date, dateTime.UtcNow).Timezone;

    private sealed record EmployeeContext(
        ONEVO.Domain.Features.CoreHr.Entities.Employee Employee,
        ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity LegalEntity);

    private sealed record EmployeeIdentity(string DisplayName);

    private sealed record PreparedRequest(
        ONEVO.Domain.Features.CoreHr.Entities.Employee Employee,
        ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity LegalEntity,
        ExpectedWorkAreaResolution Expected,
        DateOnly Date,
        string RequestedWorkArea,
        string Reason,
        EmployeeApprovalRoute Route,
        WorkAreaChangeApproverResponse Receiver);
}

internal static class ExpectedWorkAreaResolutionExtensions
{
    public static TimeZoneInfo TimezoneZone(this ExpectedWorkAreaResolution resolution)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(resolution.Timezone);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }
}

public sealed class PreviewWorkAreaChangeRequestCommandHandler(WorkAreaChangeRequestWorkflow workflow)
    : IRequestHandler<PreviewWorkAreaChangeRequestCommand, Result<WorkAreaChangeRequestPreviewResponse>>
{
    public Task<Result<WorkAreaChangeRequestPreviewResponse>> Handle(
        PreviewWorkAreaChangeRequestCommand request, CancellationToken ct)
        => workflow.PreviewAsync(request, ct);
}

public sealed class CreateWorkAreaChangeRequestCommandHandler(WorkAreaChangeRequestWorkflow workflow)
    : IRequestHandler<CreateWorkAreaChangeRequestCommand, Result<WorkAreaChangeRequestResponse>>
{
    public Task<Result<WorkAreaChangeRequestResponse>> Handle(
        CreateWorkAreaChangeRequestCommand request, CancellationToken ct)
        => workflow.CreateAsync(request, ct);
}

public sealed class ApproveWorkAreaChangeRequestCommandHandler(WorkAreaChangeRequestWorkflow workflow)
    : IRequestHandler<ApproveWorkAreaChangeRequestCommand, Result<WorkAreaChangeRequestResponse>>
{
    public Task<Result<WorkAreaChangeRequestResponse>> Handle(
        ApproveWorkAreaChangeRequestCommand request, CancellationToken ct)
        => workflow.ApproveAsync(request, ct);
}

public sealed class RejectWorkAreaChangeRequestCommandHandler(WorkAreaChangeRequestWorkflow workflow)
    : IRequestHandler<RejectWorkAreaChangeRequestCommand, Result<WorkAreaChangeRequestResponse>>
{
    public Task<Result<WorkAreaChangeRequestResponse>> Handle(
        RejectWorkAreaChangeRequestCommand request, CancellationToken ct)
        => workflow.RejectAsync(request, ct);
}

public sealed class CancelWorkAreaChangeRequestCommandHandler(WorkAreaChangeRequestWorkflow workflow)
    : IRequestHandler<CancelWorkAreaChangeRequestCommand, Result<WorkAreaChangeRequestResponse>>
{
    public Task<Result<WorkAreaChangeRequestResponse>> Handle(
        CancelWorkAreaChangeRequestCommand request, CancellationToken ct)
        => workflow.CancelAsync(request, ct);
}

public sealed class ListMyWorkAreaChangeRequestsQueryHandler(WorkAreaChangeRequestWorkflow workflow)
    : IRequestHandler<ListMyWorkAreaChangeRequestsQuery, Result<PagedResult<WorkAreaChangeRequestResponse>>>
{
    public Task<Result<PagedResult<WorkAreaChangeRequestResponse>>> Handle(
        ListMyWorkAreaChangeRequestsQuery request, CancellationToken ct)
        => workflow.ListMyAsync(request, ct);
}

public sealed class ListWorkAreaChangeRequestApprovalsQueryHandler(WorkAreaChangeRequestWorkflow workflow)
    : IRequestHandler<ListWorkAreaChangeRequestApprovalsQuery, Result<PagedResult<WorkAreaChangeRequestResponse>>>
{
    public Task<Result<PagedResult<WorkAreaChangeRequestResponse>>> Handle(
        ListWorkAreaChangeRequestApprovalsQuery request, CancellationToken ct)
        => workflow.ListApprovalsAsync(request, ct);
}
