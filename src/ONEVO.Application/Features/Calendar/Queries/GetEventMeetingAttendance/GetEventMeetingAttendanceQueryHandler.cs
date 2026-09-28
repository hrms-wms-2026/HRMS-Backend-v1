using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeetingAttendance;

public sealed class GetEventMeetingAttendanceQueryHandler(
    ICurrentUser currentUser,
    ICalendarEventMeetingRepository meetings,
    ICalendarEventMeetingAttendanceRepository attendances)
    : IRequestHandler<GetEventMeetingAttendanceQuery, Result<GetEventMeetingAttendanceResult>>
{
    public async Task<Result<GetEventMeetingAttendanceResult>> Handle(GetEventMeetingAttendanceQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<GetEventMeetingAttendanceResult>.Forbidden();

        var tenantId = currentUser.TenantId;
        var meeting = await meetings.GetTrackedByCalendarEventAsync(tenantId, request.EventId, ct);
        if (meeting is null || meeting.LastAttendanceSyncedAt is null)
            return Result<GetEventMeetingAttendanceResult>.Success(new GetEventMeetingAttendanceResult(false, []));

        var records = await attendances.GetByMeetingIdAsync(tenantId, meeting.Id, ct);
        var items = records.Select(a => new MeetingAttendeeItem(
            a.ExternalParticipantName, a.ExternalParticipantEmail, a.JoinedAt, a.LeftAt, a.DurationSeconds)).ToList();
        return Result<GetEventMeetingAttendanceResult>.Success(new GetEventMeetingAttendanceResult(true, items));
    }
}
