using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Approval.Mappers;

public static class LeaveApprovalMapper
{
    public static decimal CalculateRemaining(
        decimal totalDays,
        decimal carriedForwardDays,
        decimal usedDays,
        decimal pendingDays) =>
        totalDays + carriedForwardDays - usedDays - pendingDays;

    public static LeavePendingApprovalListItemResponse ToPendingListItem(LeavePendingApprovalListRow row) =>
        new(
            row.Request.Id,
            row.Request.EmployeeId,
            row.EmployeeName,
            row.Request.LeaveTypeId,
            row.LeaveTypeName,
            row.LeaveTypeCode,
            row.Request.StartDate,
            row.Request.EndDate,
            row.Request.TotalDays,
            row.Request.PaidDays,
            row.Request.UnpaidDays,
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
            row.Request.StartDate,
            row.Request.EndDate,
            row.Request.TotalDays,
            row.Request.Status,
            row.Request.CreatedAt);

    public static LeaveApprovalDecisionResponse ToDecision(
        LeaveRequest request,
        decimal paidDaysMoved,
        decimal remainingDays,
        string currentApproverState,
        IReadOnlyList<LeaveApprovalWarningResponse> warnings) =>
        new(
            request.Id,
            request.Status,
            currentApproverState,
            paidDaysMoved,
            request.UnpaidDays,
            remainingDays,
            warnings);
}
