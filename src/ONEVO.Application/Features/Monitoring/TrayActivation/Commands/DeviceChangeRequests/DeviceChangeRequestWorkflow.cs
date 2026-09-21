using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Queries.DeviceChangeRequests;
using ONEVO.Application.Features.Monitoring.TrayActivation.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.TrayActivation.Entities;
using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;

namespace ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;

public sealed class DeviceChangeRequestWorkflow(
    ICurrentUser currentUser,
    IDateTimeProvider dateTime,
    IEmployeeRepository employees,
    IDeviceChangeRequestRepository requests,
    ITrayActivationRepository trayActivation,
    IEmployeeAuthorityResolver authority)
{
    private const string ApprovalPermission = "attendance:approve";

    public Task<Result<DeviceChangeRequestResponse>> ApproveAsync(
        ApproveDeviceChangeRequestCommand command, CancellationToken ct)
        => DecideAsync(command.Id, DeviceChangeRequest.StatusApproved, command.ReviewComment, ct);

    public Task<Result<DeviceChangeRequestResponse>> RejectAsync(
        RejectDeviceChangeRequestCommand command, CancellationToken ct)
        => DecideAsync(command.Id, DeviceChangeRequest.StatusRejected, command.ReviewComment, ct);

    public async Task<Result<PagedResult<DeviceChangeRequestResponse>>> ListApprovalsAsync(
        ListDeviceChangeRequestApprovalsQuery query, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<PagedResult<DeviceChangeRequestResponse>>.Forbidden("Authentication is required.");
        if (!currentUser.HasPermission(ApprovalPermission))
            return Result<PagedResult<DeviceChangeRequestResponse>>.Forbidden(
                "You do not have permission to approve device changes.");

        var employee = await employees.GetDefaultForUserAsync(currentUser.TenantId, currentUser.UserId, ct);
        if (employee?.LegalEntityId is null)
            return Result<PagedResult<DeviceChangeRequestResponse>>.NotFound("Current employee record was not found.");

        var candidateEmployeeIds = await requests.ListPendingEmployeeIdsAsync(
            currentUser.TenantId, employee.LegalEntityId.Value, ct);
        var eligibleEmployeeIds = await authority.ResolveApprovalInboxScopeAsync(
            new EmployeeApprovalInboxScopeRequest(
                employee.LegalEntityId.Value, ApprovalPermission,
                EmployeeAuthorityPurpose.DeviceChangeApproval, candidateEmployeeIds), ct);

        var pageNumber = query.Paging.PageNumber < 1 ? 1 : query.Paging.PageNumber;
        var pageSize = query.Paging.PageSize < 1 ? 20 : Math.Min(query.Paging.PageSize, 100);
        var (rows, totalCount) = await requests.ListApprovalInboxAsync(
            currentUser.TenantId, employee.LegalEntityId.Value, eligibleEmployeeIds,
            (pageNumber - 1) * pageSize, pageSize, ct);
        var employeeMap = await employees.ListByIdsAsync(
            currentUser.TenantId, rows.Select(r => r.EmployeeId).Distinct().ToArray(), ct);
        var items = rows.Select(row =>
            ToResponse(row, employeeMap.TryGetValue(row.EmployeeId, out var requester) ? requester : null)).ToArray();
        return Result<PagedResult<DeviceChangeRequestResponse>>.Success(
            new PagedResult<DeviceChangeRequestResponse>(items, pageNumber, pageSize, totalCount));
    }

    private async Task<Result<DeviceChangeRequestResponse>> DecideAsync(
        Guid id, string decision, string? reviewComment, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<DeviceChangeRequestResponse>.Forbidden("Authentication is required.");
        if (!currentUser.HasPermission(ApprovalPermission))
            return Result<DeviceChangeRequestResponse>.Forbidden("You do not have permission to approve device changes.");
        if (decision == DeviceChangeRequest.StatusRejected && string.IsNullOrWhiteSpace(reviewComment))
            return Result<DeviceChangeRequestResponse>.Failure("A review comment is required when rejecting a request.");

        var request = await requests.GetTrackedByIdAsync(currentUser.TenantId, id, ct);
        if (request is null)
            return Result<DeviceChangeRequestResponse>.NotFound("Device change request was not found.");
        if (request.Status != DeviceChangeRequest.StatusPending)
            return Result<DeviceChangeRequestResponse>.Conflict("Only a pending device change request can be reviewed.");
        if (request.LegalEntityId is not Guid legalEntityId)
            return Result<DeviceChangeRequestResponse>.Conflict(
                "This device change request has no resolvable legal entity, so no approver can be determined.");

        var route = await authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
            request.EmployeeId, legalEntityId, ApprovalPermission,
            EmployeeAuthorityPurpose.DeviceChangeApproval), ct);
        if (!route.IsSuccess || route.Value is null)
            return Result<DeviceChangeRequestResponse>.Conflict("No eligible device-change approver is configured for this employee.");
        if (route.Value.ApproverUserId != currentUser.UserId)
            return Result<DeviceChangeRequestResponse>.Forbidden("You are not an eligible approver for this request.");

        var employee = await employees.GetByIdAsync(currentUser.TenantId, request.EmployeeId, ct);

        if (decision == DeviceChangeRequest.StatusApproved && request.CurrentDeviceRegistrationId is Guid oldDeviceId)
        {
            await trayActivation.DeactivateDeviceAsync(oldDeviceId, dateTime.UtcNow, ct);
            await trayActivation.RevokeAllRefreshTokensForDeviceAsync(oldDeviceId, "device_change_approved", ct);
        }

        request.Status = decision;
        request.ReviewedById = currentUser.UserId;
        request.ReviewedAt = dateTime.UtcNow;
        request.ReviewComment = string.IsNullOrWhiteSpace(reviewComment) ? null : reviewComment.Trim();
        await requests.SaveChangesAsync(ct);

        return Result<DeviceChangeRequestResponse>.Success(ToResponse(request, employee));
    }

    private static string DisplayName(EmployeeEntity? employee)
        => employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim();

    private static DeviceChangeRequestResponse ToResponse(DeviceChangeRequest request, EmployeeEntity? employee)
        => new(
            request.Id, request.EmployeeId, DisplayName(employee),
            request.NewDeviceName, request.NewDeviceOs, request.Status,
            request.RequestedAt, request.ReviewedById, request.ReviewedAt, request.ReviewComment);
}

public sealed class ApproveDeviceChangeRequestCommandHandler(DeviceChangeRequestWorkflow workflow)
    : IRequestHandler<ApproveDeviceChangeRequestCommand, Result<DeviceChangeRequestResponse>>
{
    public Task<Result<DeviceChangeRequestResponse>> Handle(ApproveDeviceChangeRequestCommand request, CancellationToken ct)
        => workflow.ApproveAsync(request, ct);
}

public sealed class RejectDeviceChangeRequestCommandHandler(DeviceChangeRequestWorkflow workflow)
    : IRequestHandler<RejectDeviceChangeRequestCommand, Result<DeviceChangeRequestResponse>>
{
    public Task<Result<DeviceChangeRequestResponse>> Handle(RejectDeviceChangeRequestCommand request, CancellationToken ct)
        => workflow.RejectAsync(request, ct);
}

public sealed class ListDeviceChangeRequestApprovalsQueryHandler(DeviceChangeRequestWorkflow workflow)
    : IRequestHandler<ListDeviceChangeRequestApprovalsQuery, Result<PagedResult<DeviceChangeRequestResponse>>>
{
    public Task<Result<PagedResult<DeviceChangeRequestResponse>>> Handle(ListDeviceChangeRequestApprovalsQuery request, CancellationToken ct)
        => workflow.ListApprovalsAsync(request, ct);
}
