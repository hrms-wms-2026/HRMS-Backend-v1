using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries.LocationChangeRequests;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using CoreEmployeeRepository = ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces.IEmployeeRepository;

namespace ONEVO.Application.Features.TimeAttendance.Commands.LocationChangeRequests;

/// <summary>
/// Request/approve/reject/apply workflow for an employee's persistent remote work location.
/// Deliberately separate from WorkAreaChangeRequestWorkflow (that one is date-scoped, a one-day
/// onsite/remote override) but reuses the same approver-resolution mechanism via
/// IEmployeeAuthorityResolver, just under its own EmployeeAuthorityPurpose.LocationChangeApproval.
/// </summary>
public sealed class LocationChangeRequestWorkflow(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    CoreEmployeeRepository employees,
    ILegalEntityRepository legalEntities,
    ILocationChangeRequestRepository requests,
    IEmployeeWorkLocationRepository workLocations,
    IEmployeeAuthorityResolver authority,
    IUnitOfWork unitOfWork)
{
    private const string ApprovalPermission = "attendance:approve";

    public async Task<Result<LocationChangeRequestResponse>> CreateAsync(
        CreateLocationChangeRequestCommand command, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<LocationChangeRequestResponse>.Forbidden("Authentication is required.");

        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<LocationChangeRequestResponse>.Failure(context.Error!, context.StatusCode ?? 400);

        var value = context.Value!;
        return await CreateAsync(
            currentUser.TenantId, value.Employee, value.LegalEntity,
            command.Latitude, command.Longitude, command.AccuracyMeters, command.Reason, ct);
    }

    /// <summary>Same as above but for a tray-device-authenticated caller (no ICurrentUser) - the
    /// tray already resolved the employee/legal entity itself and passes them explicitly.</summary>
    public async Task<Result<LocationChangeRequestResponse>> CreateAsync(
        Guid tenantId, EmployeeEntity employee, ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity legalEntity,
        double latitude, double longitude, double? accuracyMeters, string reason, CancellationToken ct)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
            {
                if (string.IsNullOrWhiteSpace(reason))
                    return Result<LocationChangeRequestResponse>.Failure("A reason is required.");
                if (await requests.HasActiveAsync(tenantId, employee.Id, transactionCt))
                    return Result<LocationChangeRequestResponse>.Conflict(
                        "An active location change request already exists for this employee.");

                var routeResult = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
                    employee.Id, legalEntity.Id, ApprovalPermission,
                    EmployeeAuthorityPurpose.LocationChangeApproval), transactionCt);
                if (!routeResult.IsSuccess || routeResult.Value is null)
                    return Result<LocationChangeRequestResponse>.Conflict(
                        "No eligible location-change approver is configured for this employee.");

                var request = new LocationChangeRequest
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EmployeeId = employee.Id,
                    LegalEntityId = legalEntity.Id,
                    RequestedLatitude = latitude,
                    RequestedLongitude = longitude,
                    RequestedAccuracyMeters = accuracyMeters,
                    Reason = reason.Trim(),
                    Status = LocationChangeRequest.StatusPending,
                    RequestedAt = dateTime.UtcNow
                };
                await requests.AddAsync(request, transactionCt);
                await requests.SaveChangesAsync(transactionCt);

                return Result<LocationChangeRequestResponse>.Success(ToResponse(request, employee));
            }, ct);
        }
        catch (UniqueConstraintConflictException)
        {
            return Result<LocationChangeRequestResponse>.Conflict(
                "An active location change request already exists for this employee.");
        }
    }

    public Task<Result<LocationChangeRequestResponse>> ApproveAsync(
        ApproveLocationChangeRequestCommand command, CancellationToken ct)
        => DecideAsync(command.Id, LocationChangeRequest.StatusApproved, command.ReviewComment, ct);

    public Task<Result<LocationChangeRequestResponse>> RejectAsync(
        RejectLocationChangeRequestCommand command, CancellationToken ct)
        => DecideAsync(command.Id, LocationChangeRequest.StatusRejected, command.ReviewComment, ct);

    public async Task<Result<LocationChangeRequestResponse>> CancelAsync(
        CancelLocationChangeRequestCommand command, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<LocationChangeRequestResponse>.Forbidden("Authentication is required.");

        var request = await requests.GetTrackedByIdAsync(currentUser.TenantId, command.Id, ct);
        if (request is null)
            return Result<LocationChangeRequestResponse>.NotFound("Location change request was not found.");

        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<LocationChangeRequestResponse>.Failure(context.Error!, context.StatusCode ?? 400);
        if (context.Value!.Employee.Id != request.EmployeeId)
            return Result<LocationChangeRequestResponse>.Forbidden("Only the requester can cancel this request.");
        if (request.Status != LocationChangeRequest.StatusPending)
            return Result<LocationChangeRequestResponse>.Conflict("Only a pending location change request can be cancelled.");

        request.Status = LocationChangeRequest.StatusCancelled;
        request.ReviewedById = currentUser.UserId;
        request.ReviewedAt = dateTime.UtcNow;
        await requests.SaveChangesAsync(ct);

        return Result<LocationChangeRequestResponse>.Success(ToResponse(request, context.Value.Employee));
    }

    /// <summary>The employee's answer to the post-clock-in "save this as your new location?"
    /// prompt, from a tenant-user-authenticated (web) caller. Apply=false is a valid, expected
    /// outcome (not an error) - it just means the request stays approved and keeps re-prompting
    /// on the next clock-in.</summary>
    public async Task<Result<LocationChangeRequestResponse>> RespondAsync(
        RespondToLocationChangeRequestCommand command, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<LocationChangeRequestResponse>.Forbidden("Authentication is required.");

        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<LocationChangeRequestResponse>.Failure(context.Error!, context.StatusCode ?? 400);

        return await RespondAsync(currentUser.TenantId, context.Value!.Employee.Id, command.Id, command.Apply, ct);
    }

    /// <summary>Same as above but for a tray-device-authenticated caller, which has no
    /// ICurrentUser - the tray already resolved its own employeeId (via ITrayCurrentDevice) and
    /// passes it explicitly instead.</summary>
    public async Task<Result<LocationChangeRequestResponse>> RespondAsync(
        Guid tenantId, Guid employeeId, Guid requestId, bool apply, CancellationToken ct)
    {
        return await unitOfWork.ExecuteInTransactionAsync(async transactionCt =>
        {
            var request = await requests.GetTrackedByIdAsync(tenantId, requestId, transactionCt);
            if (request is null)
                return Result<LocationChangeRequestResponse>.NotFound("Location change request was not found.");
            if (request.EmployeeId != employeeId)
                return Result<LocationChangeRequestResponse>.Forbidden("Only the requester can respond to this request.");
            if (request.Status != LocationChangeRequest.StatusApproved)
                return Result<LocationChangeRequestResponse>.Conflict("Only an approved location change request can be responded to.");

            if (apply)
            {
                var existing = await workLocations.GetByEmployeeIdAsync(tenantId, request.EmployeeId, transactionCt);
                var now = dateTime.UtcNow;
                if (existing is null)
                {
                    await workLocations.AddAsync(new Domain.Features.TimeAttendance.Entities.EmployeeWorkLocation
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        EmployeeId = request.EmployeeId,
                        Latitude = request.RequestedLatitude,
                        Longitude = request.RequestedLongitude,
                        AccuracyMeters = request.RequestedAccuracyMeters,
                        RegisteredAt = now,
                        UpdatedAt = now
                    }, transactionCt);
                }
                else
                {
                    existing.Latitude = request.RequestedLatitude;
                    existing.Longitude = request.RequestedLongitude;
                    existing.AccuracyMeters = request.RequestedAccuracyMeters;
                    existing.UpdatedAt = now;
                    workLocations.Update(existing);
                }

                request.Status = LocationChangeRequest.StatusApplied;
                request.AppliedAt = now;
            }
            // apply == false: leave the request "approved" and everything else untouched - the
            // tray will ask again on the employee's next clock-in.

            await requests.SaveChangesAsync(transactionCt);
            var employee = await employees.GetByIdAsync(tenantId, request.EmployeeId, transactionCt);
            return Result<LocationChangeRequestResponse>.Success(ToResponse(request, employee));
        }, ct);
    }

    public async Task<Result<PagedResult<LocationChangeRequestResponse>>> ListMyAsync(
        ListMyLocationChangeRequestsQuery query, CancellationToken ct)
    {
        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<PagedResult<LocationChangeRequestResponse>>.Failure(context.Error!, context.StatusCode ?? 400);

        var pageNumber = query.Paging.PageNumber < 1 ? 1 : query.Paging.PageNumber;
        var pageSize = query.Paging.PageSize < 1 ? 20 : Math.Min(query.Paging.PageSize, 100);
        var (rows, totalCount) = await requests.ListMyAsync(
            currentUser.TenantId, context.Value!.Employee.Id, query.Status, (pageNumber - 1) * pageSize, pageSize, ct);
        var items = rows.Select(row => ToResponse(row, context.Value.Employee)).ToArray();
        return Result<PagedResult<LocationChangeRequestResponse>>.Success(
            new PagedResult<LocationChangeRequestResponse>(items, pageNumber, pageSize, totalCount));
    }

    public async Task<Result<PagedResult<LocationChangeRequestResponse>>> ListApprovalsAsync(
        ListLocationChangeRequestApprovalsQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<PagedResult<LocationChangeRequestResponse>>.Forbidden("Authentication is required.");
        if (!currentUser.HasPermission(ApprovalPermission))
            return Result<PagedResult<LocationChangeRequestResponse>>.Forbidden(
                "You do not have permission to approve location changes.");

        var context = await ResolveEmployeeContextAsync(ct);
        if (!context.IsSuccess)
            return Result<PagedResult<LocationChangeRequestResponse>>.Failure(context.Error!, context.StatusCode ?? 400);

        var value = context.Value!;
        var candidateEmployeeIds = await requests.ListPendingEmployeeIdsAsync(currentUser.TenantId, value.LegalEntity.Id, ct);
        var eligibleEmployeeIds = await authority.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(
                value.LegalEntity.Id, ApprovalPermission, EmployeeAuthorityPurpose.LocationChangeApproval, candidateEmployeeIds), ct);

        var pageNumber = query.Paging.PageNumber < 1 ? 1 : query.Paging.PageNumber;
        var pageSize = query.Paging.PageSize < 1 ? 20 : Math.Min(query.Paging.PageSize, 100);
        var (rows, totalCount) = await requests.ListApprovalInboxAsync(
            currentUser.TenantId, value.LegalEntity.Id, eligibleEmployeeIds, (pageNumber - 1) * pageSize, pageSize, ct);
        var employeeMap = await employees.ListByIdsAsync(currentUser.TenantId, rows.Select(r => r.EmployeeId).Distinct().ToArray(), ct);
        var items = rows.Select(row =>
            ToResponse(row, employeeMap.TryGetValue(row.EmployeeId, out var requester) ? requester : null)).ToArray();
        return Result<PagedResult<LocationChangeRequestResponse>>.Success(
            new PagedResult<LocationChangeRequestResponse>(items, pageNumber, pageSize, totalCount));
    }

    private async Task<Result<EmployeeContext>> ResolveEmployeeContextAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<EmployeeContext>.Forbidden("Authentication is required.");
        if (currentUser.TenantId == Guid.Empty)
            return Result<EmployeeContext>.Forbidden("Tenant context is missing.");

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee?.LegalEntityId is null)
            return Result<EmployeeContext>.NotFound("Current employee record was not found.");
        var legalEntity = await legalEntities.GetByIdForTenantAsync(currentUser.TenantId, employee.LegalEntityId.Value, ct);
        return legalEntity is null
            ? Result<EmployeeContext>.NotFound("Company was not found.")
            : Result<EmployeeContext>.Success(new EmployeeContext(employee, legalEntity));
    }

    private static string DisplayName(EmployeeEntity? employee)
        => employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim();

    private static LocationChangeRequestResponse ToResponse(LocationChangeRequest request, EmployeeEntity? employee)
        => new(
            request.Id,
            request.EmployeeId,
            DisplayName(employee),
            request.RequestedLatitude,
            request.RequestedLongitude,
            request.RequestedAccuracyMeters,
            request.Reason,
            request.Status,
            request.RequestedAt,
            request.ReviewedById,
            request.ReviewedAt,
            request.ReviewComment,
            request.AppliedAt);

    private async Task<Result<LocationChangeRequestResponse>> DecideAsync(
        Guid id, string decision, string? reviewComment, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<LocationChangeRequestResponse>.Forbidden("Authentication is required.");
        if (!currentUser.HasPermission(ApprovalPermission))
            return Result<LocationChangeRequestResponse>.Forbidden("You do not have permission to approve location changes.");
        if (decision == LocationChangeRequest.StatusRejected && string.IsNullOrWhiteSpace(reviewComment))
            return Result<LocationChangeRequestResponse>.Failure("A review comment is required when rejecting a request.");

        var request = await requests.GetTrackedByIdAsync(currentUser.TenantId, id, ct);
        if (request is null)
            return Result<LocationChangeRequestResponse>.NotFound("Location change request was not found.");
        if (request.Status != LocationChangeRequest.StatusPending)
            return Result<LocationChangeRequestResponse>.Conflict("Only a pending location change request can be reviewed.");

        var route = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            request.EmployeeId, request.LegalEntityId, ApprovalPermission,
            EmployeeAuthorityPurpose.LocationChangeApproval), ct);
        if (!route.IsSuccess || route.Value is null)
            return Result<LocationChangeRequestResponse>.Conflict("No eligible location-change approver is configured for this employee.");
        if (route.Value.ApproverUserId != currentUser.UserId)
            return Result<LocationChangeRequestResponse>.Forbidden("You are not an eligible approver for this request.");

        var employee = await employees.GetByIdAsync(currentUser.TenantId, request.EmployeeId, ct);

        request.Status = decision;
        request.ReviewedById = currentUser.UserId;
        request.ReviewedAt = dateTime.UtcNow;
        request.ReviewComment = string.IsNullOrWhiteSpace(reviewComment) ? null : reviewComment.Trim();
        await requests.SaveChangesAsync(ct);

        return Result<LocationChangeRequestResponse>.Success(ToResponse(request, employee));
    }

    private sealed record EmployeeContext(EmployeeEntity Employee, ONEVO.Domain.Features.OrgStructure.Entities.LegalEntity LegalEntity);
}

public sealed class CreateLocationChangeRequestCommandHandler(LocationChangeRequestWorkflow workflow)
    : IRequestHandler<CreateLocationChangeRequestCommand, Result<LocationChangeRequestResponse>>
{
    public Task<Result<LocationChangeRequestResponse>> Handle(CreateLocationChangeRequestCommand request, CancellationToken ct)
        => workflow.CreateAsync(request, ct);
}

public sealed class ApproveLocationChangeRequestCommandHandler(LocationChangeRequestWorkflow workflow)
    : IRequestHandler<ApproveLocationChangeRequestCommand, Result<LocationChangeRequestResponse>>
{
    public Task<Result<LocationChangeRequestResponse>> Handle(ApproveLocationChangeRequestCommand request, CancellationToken ct)
        => workflow.ApproveAsync(request, ct);
}

public sealed class RejectLocationChangeRequestCommandHandler(LocationChangeRequestWorkflow workflow)
    : IRequestHandler<RejectLocationChangeRequestCommand, Result<LocationChangeRequestResponse>>
{
    public Task<Result<LocationChangeRequestResponse>> Handle(RejectLocationChangeRequestCommand request, CancellationToken ct)
        => workflow.RejectAsync(request, ct);
}

public sealed class CancelLocationChangeRequestCommandHandler(LocationChangeRequestWorkflow workflow)
    : IRequestHandler<CancelLocationChangeRequestCommand, Result<LocationChangeRequestResponse>>
{
    public Task<Result<LocationChangeRequestResponse>> Handle(CancelLocationChangeRequestCommand request, CancellationToken ct)
        => workflow.CancelAsync(request, ct);
}

public sealed class RespondToLocationChangeRequestCommandHandler(LocationChangeRequestWorkflow workflow)
    : IRequestHandler<RespondToLocationChangeRequestCommand, Result<LocationChangeRequestResponse>>
{
    public Task<Result<LocationChangeRequestResponse>> Handle(RespondToLocationChangeRequestCommand request, CancellationToken ct)
        => workflow.RespondAsync(request, ct);
}

public sealed class ListMyLocationChangeRequestsQueryHandler(LocationChangeRequestWorkflow workflow)
    : IRequestHandler<ListMyLocationChangeRequestsQuery, Result<PagedResult<LocationChangeRequestResponse>>>
{
    public Task<Result<PagedResult<LocationChangeRequestResponse>>> Handle(ListMyLocationChangeRequestsQuery request, CancellationToken ct)
        => workflow.ListMyAsync(request, ct);
}

public sealed class ListLocationChangeRequestApprovalsQueryHandler(LocationChangeRequestWorkflow workflow)
    : IRequestHandler<ListLocationChangeRequestApprovalsQuery, Result<PagedResult<LocationChangeRequestResponse>>>
{
    public Task<Result<PagedResult<LocationChangeRequestResponse>>> Handle(ListLocationChangeRequestApprovalsQuery request, CancellationToken ct)
        => workflow.ListApprovalsAsync(request, ct);
}
