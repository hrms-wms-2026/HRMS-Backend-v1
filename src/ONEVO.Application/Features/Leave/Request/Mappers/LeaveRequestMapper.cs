using System.Text.Json;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;
using ONEVO.Application.Features.Leave.Request.Services;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Request.Mappers;

public static class LeaveRequestMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static LeaveRequestBalanceImpactResponse ToBalanceImpact(
        decimal currentRemainingDays,
        decimal currentPendingDays,
        decimal paidDays) =>
        new(
            CurrentRemainingDays: currentRemainingDays,
            PendingAfterSubmitDays: currentPendingDays + paidDays,
            RemainingAfterSubmitDays: currentRemainingDays - paidDays);

    public static LeaveRequestListItemResponse ToListItem(LeaveRequest request, string leaveTypeName, string leaveTypeCode) =>
        new(
            request.Id,
            request.EmployeeId,
            request.LeaveTypeId,
            leaveTypeName,
            leaveTypeCode,
            request.StartDate,
            request.EndDate,
            request.TotalDays,
            request.PaidDays,
            request.UnpaidDays,
            request.Status,
            request.NoticePeriodMissed,
            request.CreatedAt,
            request.UpdatedAt);

    public static string ToConflictSnapshotJson(
        IReadOnlyList<LeaveRequestWarningResponse> warnings,
        IReadOnlyList<LeaveRequestCalendarConflict> calendarConflicts,
        decimal? teamAbsencePercent) =>
        JsonSerializer.Serialize(
            new LeaveRequestConflictSnapshotResponse(
                warnings,
                calendarConflicts.Select(c => new LeaveRequestCalendarConflictResponse(
                    c.Source, c.Title, c.StartsAt, c.EndsAt)).ToList(),
                teamAbsencePercent),
            JsonOptions);

    public static LeaveRequestResponse ToResponse(
        LeaveRequest request,
        string leaveTypeName,
        string leaveTypeCode,
        IReadOnlyList<LeaveRequestApprover> approvers,
        LeaveRequestBalanceImpactResponse balanceImpact,
        LeaveRequestConflictSnapshotResponse snapshot) =>
        new(
            request.Id,
            request.EmployeeId,
            request.LeaveTypeId,
            leaveTypeName,
            leaveTypeCode,
            request.StartDate,
            request.EndDate,
            request.HalfDayPeriod,
            request.TotalDays,
            request.PaidDays,
            request.UnpaidDays,
            request.Status,
            request.NoticePeriodMissed,
            request.SubmittedOnBehalfOfBy,
            balanceImpact,
            approvers.Select(a => new LeaveRequestApproverResponse(
                a.ApproverEmployeeId, a.SequenceOrder, a.Status, a.DelegatedFromApproverId)).ToList(),
            snapshot,
            request.CreatedAt);
}
