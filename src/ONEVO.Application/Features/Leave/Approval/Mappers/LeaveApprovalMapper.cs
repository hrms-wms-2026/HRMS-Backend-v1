using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Approval.Mappers;

public static class LeaveApprovalMapper
{
    public static decimal CalculateRemaining(
        decimal totalHours,
        decimal carriedForwardHours,
        decimal usedHours,
        decimal pendingHours) =>
        totalHours + carriedForwardHours - usedHours - pendingHours;

    public static LeavePendingApprovalListItemResponse ToPendingListItem(LeavePendingApprovalListRow row) =>
        new(
            row.Request.Id,
            row.Request.EmployeeId,
            row.EmployeeName,
            row.Request.LeaveTypeId,
            row.LeaveTypeName,
            row.LeaveTypeCode,
            row.Request.StartAt,
            row.Request.EndAt,
            row.Request.TotalHours,
            row.Request.PaidHours,
            row.Request.UnpaidHours,
            row.Request.Status,
            row.Request.CreatedAt);

    public static LeaveRequestAllListItemResponse ToAllListItem(LeaveRequestAllListRow row) =>
        new(
            row.Request.Id,
            row.Request.EmployeeId,
            row.EmployeeName,
            row.DepartmentId,
            row.DepartmentName,
            row.Request.LeaveTypeId,
            row.LeaveTypeName,
            row.Request.StartAt,
            row.Request.EndAt,
            row.Request.TotalHours,
            row.Request.Status,
            row.Request.CreatedAt);

    public static LeaveApprovalDecisionResponse ToDecision(
        LeaveRequest request,
        decimal paidHoursMoved,
        decimal remainingHours,
        string currentApproverState,
        IReadOnlyList<LeaveApprovalWarningResponse> warnings) =>
        new(
            request.Id,
            request.Status,
            currentApproverState,
            paidHoursMoved,
            request.UnpaidHours,
            remainingHours,
            warnings);
}
