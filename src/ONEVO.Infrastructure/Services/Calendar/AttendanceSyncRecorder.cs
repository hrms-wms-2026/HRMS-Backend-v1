using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

/// <summary>One attendance row as both providers report it.</summary>
internal sealed record FetchedAttendance(string? Name, string? Email, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt);

/// <summary>Applies <see cref="AttendanceSyncPolicy"/> to one fetch and persists the result. Shared by
/// the Teams and Zoom jobs so both classify identically.</summary>
internal static class AttendanceSyncRecorder
{
    public static async Task RecordAsync(
        Guid tenantId, DueAttendanceSync due, AttendanceFetchOutcome outcome, IReadOnlyList<FetchedAttendance> records,
        DateTimeOffset now, ICalendarEventMeetingAttendanceRepository attendances, ICalendarEventMeetingRepository meetings,
        IUnitOfWork unitOfWork, ILogger logger, CancellationToken ct)
    {
        var status = AttendanceSyncPolicy.NextStatus(outcome, due.EventEndDate, now);
        if (status is null)
            return; // not decided yet: retried next run

        var meeting = due.Meeting;
        if (status == CalendarAttendanceSyncStatuses.Synced && records.Count > 0)
        {
            await attendances.AddRangeAsync(records.Select(r => new CalendarEventMeetingAttendance
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CalendarEventMeetingId = meeting.Id,
                ExternalParticipantName = r.Name, ExternalParticipantEmail = r.Email,
                JoinedAt = r.JoinedAt, LeftAt = r.LeftAt,
                DurationSeconds = r.LeftAt.HasValue ? (int)(r.LeftAt.Value - r.JoinedAt).TotalSeconds : null,
                CreatedAt = now
            }), ct);
        }

        meeting.AttendanceSyncStatus = status;
        meeting.LastAttendanceSyncedAt = status == CalendarAttendanceSyncStatuses.Synced ? now : null;
        meeting.UpdatedAt = now;
        meetings.Update(meeting);
        await unitOfWork.SaveChangesAsync(ct);

        if (status != CalendarAttendanceSyncStatuses.Synced)
            logger.LogWarning(
                "Attendance for meeting {MeetingId} (tenant {TenantId}) recorded as {Status} after {Hours} h.",
                meeting.Id, tenantId, status, AttendanceSyncPolicy.GiveUpAfter.TotalHours);
    }
}
