using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Commands;

public sealed record RecheckMeetingAttendanceCommand(Guid SeriesId, DateTimeOffset OriginalStart) : IRequest<Result<MeetingAttendanceStatusResponse>>;

/// <summary>POST .../attendance/recheck (spec 5.8): resolve the caller's employee, then <see cref="MeetingAttendanceSyncService"/>.</summary>
public sealed class MeetingAttendanceCommandHandlers : IRequestHandler<RecheckMeetingAttendanceCommand, Result<MeetingAttendanceStatusResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly MeetingAttendanceSyncService _attendance;

    public MeetingAttendanceCommandHandlers(ICurrentUser currentUser, ICallerIdentityResolver identity, MeetingAttendanceSyncService attendance)
    {
        _currentUser = currentUser;
        _identity = identity;
        _attendance = attendance;
    }

    public async Task<Result<MeetingAttendanceStatusResponse>> Handle(RecheckMeetingAttendanceCommand c, CancellationToken ct)
        => _currentUser.IsAuthenticated
           && await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) is Guid caller
            ? await _attendance.RecheckAsync(_currentUser.TenantId, caller, c.SeriesId, c.OriginalStart, ct)
            : MeetingReadService.NotFound<MeetingAttendanceStatusResponse>();
}
