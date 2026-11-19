using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Queries;

/// <summary>GET /api/v1/employees/{employeeId}/meeting-attendance (spec D16, D17): the reporting-line attendance view.</summary>
public sealed record EmployeeMeetingAttendanceQuery(Guid EmployeeId, DateTimeOffset? From, DateTimeOffset? To)
    : IRequest<Result<EmployeeMeetingAttendanceResponse>>;

/// <summary>The reporting-line attendance read: resolve the caller's employee, then
/// <see cref="EmployeeMeetingAttendanceService"/>. A caller without an employee record gets the outsider's 404.</summary>
public sealed class EmployeeMeetingAttendanceQueryHandler
    : IRequestHandler<EmployeeMeetingAttendanceQuery, Result<EmployeeMeetingAttendanceResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly EmployeeMeetingAttendanceService _attendance;

    public EmployeeMeetingAttendanceQueryHandler(ICurrentUser currentUser, ICallerIdentityResolver identity,
        EmployeeMeetingAttendanceService attendance)
    {
        _currentUser = currentUser;
        _identity = identity;
        _attendance = attendance;
    }

    public async Task<Result<EmployeeMeetingAttendanceResponse>> Handle(EmployeeMeetingAttendanceQuery q, CancellationToken ct)
        => _currentUser.IsAuthenticated
           && await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) is Guid caller
            ? await _attendance.GetAsync(_currentUser.TenantId, caller, q.EmployeeId, q.From, q.To, ct)
            : MeetingReadService.NotFound<EmployeeMeetingAttendanceResponse>();
}
