using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Queries.GetPendingAttendanceApprovals;

public sealed class GetPendingAttendanceApprovalsQueryHandler
    : IRequestHandler<
        GetPendingAttendanceApprovalsQuery,
        Result<PendingAttendanceApprovalsDto>>
{
    private readonly ITimeAttendanceRepository _attendance;
    private readonly IVerificationRepository _verification;

    public GetPendingAttendanceApprovalsQueryHandler(
        ITimeAttendanceRepository attendance,
        IVerificationRepository verification)
    {
        _attendance = attendance;
        _verification = verification;
    }

    public async Task<Result<PendingAttendanceApprovalsDto>> Handle(
        GetPendingAttendanceApprovalsQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var skip = (page - 1) * pageSize;
        var workArea = await _attendance.GetPendingWorkAreaChangesAsync(
            skip,
            pageSize,
            cancellationToken);
        var remote = await _verification.GetPendingRemoteChangesAsync(
            skip,
            pageSize,
            cancellationToken);

        return Result<PendingAttendanceApprovalsDto>.Success(
            new PendingAttendanceApprovalsDto(
                workArea.Select(requestItem => new WorkAreaApprovalDto(
                    requestItem.Id,
                    requestItem.EmployeeId,
                    requestItem.Date,
                    requestItem.CurrentExpectedWorkArea,
                    requestItem.RequestedWorkArea,
                    requestItem.Reason,
                    requestItem.RequestedAt,
                    requestItem.Version)).ToArray(),
                remote.Select(requestItem => new RemoteLocationApprovalDto(
                    requestItem.Id,
                    requestItem.EmployeeId,
                    requestItem.Reason,
                    requestItem.RequestedAt,
                    requestItem.Version)).ToArray(),
                page,
                pageSize));
    }
}

