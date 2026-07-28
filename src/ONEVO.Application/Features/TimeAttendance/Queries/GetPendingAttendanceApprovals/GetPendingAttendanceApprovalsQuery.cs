using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Queries.GetPendingAttendanceApprovals;

public sealed record GetPendingAttendanceApprovalsQuery(
    int Page,
    int PageSize) : IRequest<Result<PendingAttendanceApprovalsDto>>;

public sealed record PendingAttendanceApprovalsDto(
    IReadOnlyList<WorkAreaApprovalDto> WorkAreaChanges,
    IReadOnlyList<RemoteLocationApprovalDto> RemoteLocationChanges,
    int Page,
    int PageSize);

public sealed record WorkAreaApprovalDto(
    Guid Id,
    Guid EmployeeId,
    DateOnly Date,
    string CurrentExpectedWorkArea,
    string RequestedWorkArea,
    string Reason,
    DateTimeOffset RequestedAt,
    uint Version);

public sealed record RemoteLocationApprovalDto(
    Guid Id,
    Guid EmployeeId,
    string Reason,
    DateTimeOffset RequestedAt,
    uint Version);

