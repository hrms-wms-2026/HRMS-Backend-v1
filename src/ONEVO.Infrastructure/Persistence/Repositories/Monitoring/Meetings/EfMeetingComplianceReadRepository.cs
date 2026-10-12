using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings;

public sealed class EfMeetingComplianceReadRepository(ApplicationDbContext db) : IMeetingComplianceReadRepository
{
    public async Task<IReadOnlyList<MeetingApplicationComplianceItem>> ListAsync(
        Guid tenantId,
        Guid employeeId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        CancellationToken ct)
    {
        var meetingProcesses = await db.GlobalApplications.AsNoTracking()
            .Where(x => x.Category == GlobalApplicationCategories.Meeting)
            .Select(x => x.ProcessName)
            .ToListAsync(ct);

        var meetings = await (
            from participant in db.CalendarEventParticipants.AsNoTracking()
            join calendarEvent in db.PersonalCalendarEvents.AsNoTracking()
                on participant.EventId equals calendarEvent.Id
            where participant.TenantId == tenantId
                  && participant.EmployeeId == employeeId
                  && participant.ResponseStatus != CalendarEventParticipantStatuses.Rejected
                  && calendarEvent.EndDate > fromUtc
                  && calendarEvent.StartDate < toUtcExclusive
                  && (calendarEvent.EventStatus == null || calendarEvent.EventStatus != CalendarEventStatuses.Cancelled)
                  && (calendarEvent.MeetingLink != null ||
                      db.CalendarEventMeetings.Any(m =>
                          m.TenantId == tenantId &&
                          m.CalendarEventId == calendarEvent.Id &&
                          m.Status == CalendarEventMeetingStatuses.Active))
            orderby calendarEvent.StartDate
            select new
            {
                calendarEvent.Id,
                calendarEvent.Title,
                calendarEvent.StartDate,
                calendarEvent.EndDate
            }).ToListAsync(ct);

        if (meetings.Count == 0)
            return [];

        var normalizedProcesses = meetingProcesses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sampleFrom = meetings.Min(x => x.StartDate);
        var sampleTo = meetings.Max(x => x.EndDate);
        var samples = normalizedProcesses.Count == 0
            ? []
            : await db.AppUsageSnapshots.AsNoTracking()
                .Where(x => x.TenantId == tenantId
                            && x.EmployeeId == employeeId
                            && x.ProcessName != null
                            && x.CapturedAt >= sampleFrom
                            && x.CapturedAt < sampleTo)
                .Select(x => new { x.ProcessName, x.CapturedAt })
                .ToListAsync(ct);

        return meetings.Select(meeting =>
        {
            var startsAt = meeting.StartDate;
            var endsAt = meeting.EndDate;
            var durationSeconds = Math.Max(0, (int)Math.Ceiling((endsAt - startsAt).TotalSeconds));
            var matchingSamples = samples.Count(sample =>
                sample.ProcessName is not null
                && normalizedProcesses.Contains(GlobalApplication.NormalizeProcessName(sample.ProcessName))
                && sample.CapturedAt >= startsAt
                && sample.CapturedAt < endsAt);
            var result = MeetingApplicationComplianceCalculator.Calculate(durationSeconds, matchingSamples);

            return new MeetingApplicationComplianceItem(
                meeting.Id,
                meeting.Title,
                meeting.StartDate,
                meeting.EndDate,
                result.RequiredSeconds,
                result.UsedSeconds,
                result.UsagePercentage,
                result.IsCompliant);
        }).ToList();
    }
}
