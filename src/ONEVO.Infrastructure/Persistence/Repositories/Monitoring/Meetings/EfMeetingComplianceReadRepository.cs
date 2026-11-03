using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.DevPlatform.AppCatalog.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.Monitoring.Meetings;

/// <summary>
/// The monitoring compliance view (spec D17): every meeting of the monitored employee in the window - the legacy
/// calendar events and the task-meeting occurrences - with the share of the meeting the meeting application was used
/// for. A private meeting shows as "Private meeting"; its usage numbers are unchanged.
/// </summary>
public sealed class EfMeetingComplianceReadRepository(
    ApplicationDbContext db,
    IMeetingRepository meetings) : IMeetingComplianceReadRepository
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

        var legacy = await (
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
                calendarEvent.IsPrivate,
                calendarEvent.StartDate,
                calendarEvent.EndDate,
                // Set when the meeting was created through ONEVO (Teams/Zoom); null for a pasted link.
                Provider = db.CalendarEventMeetings
                    .Where(m => m.TenantId == tenantId && m.CalendarEventId == calendarEvent.Id && m.Status == CalendarEventMeetingStatuses.Active)
                    .Select(m => m.Provider)
                    .FirstOrDefault()
            }).ToListAsync(ct);

        // The task meetings of the same window: the occurrences the employee effectively attends, has not declined,
        // and whose link is active (a link that is not active has no provider application to measure).
        var taskMeetings = await meetings.ListEmployeeTaskMeetingsAsync(tenantId, employeeId, fromUtc, toUtcExclusive, ct);

        var items = legacy
            .Select(m => new ComplianceMeeting(m.Id, m.Title, m.IsPrivate, m.StartDate, m.EndDate, m.Provider))
            .Concat(taskMeetings.Select(m => new ComplianceMeeting(m.TaskId, m.Title, m.IsPrivate, m.StartAt, m.EndAt, m.Provider)))
            .ToList();

        if (items.Count == 0)
            return [];

        var categoryStems = meetingProcesses.Select(MeetingProviderApplications.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var anyProviderMeeting = items.Any(m => MeetingProviderApplications.ProcessStemsFor(m.Provider) is not null);
        var sampleFrom = items.Min(x => x.StartsAt);
        var sampleTo = items.Max(x => x.EndsAt);
        var samples = categoryStems.Count == 0 && !anyProviderMeeting
            ? []
            : await db.AppUsageSnapshots.AsNoTracking()
                .Where(x => x.TenantId == tenantId
                            && x.EmployeeId == employeeId
                            && x.ProcessName != null
                            && x.CapturedAt >= sampleFrom
                            && x.CapturedAt < sampleTo)
                .Select(x => new { x.ProcessName, x.CapturedAt })
                .ToListAsync(ct);

        return items.Select(meeting =>
        {
            var startsAt = meeting.StartsAt;
            var endsAt = meeting.EndsAt;
            var durationSeconds = Math.Max(0, (int)Math.Ceiling((endsAt - startsAt).TotalSeconds));
            // A meeting created through ONEVO counts only its own provider's app; a pasted link counts any Meeting-category app.
            var stems = MeetingProviderApplications.ProcessStemsFor(meeting.Provider) ?? categoryStems;
            var matchingSamples = samples.Count(sample =>
                sample.ProcessName is not null
                && stems.Contains(MeetingProviderApplications.Stem(sample.ProcessName))
                && sample.CapturedAt >= startsAt
                && sample.CapturedAt < endsAt);
            var result = MeetingApplicationComplianceCalculator.Calculate(durationSeconds, matchingSamples);

            return new MeetingApplicationComplianceItem(
                meeting.EventId,
                // A private meeting is listed, never named (spec D17). Its usage numbers are unchanged.
                meeting.IsPrivate ? MeetingPrivacy.MaskedTitle : meeting.Title,
                meeting.StartsAt,
                meeting.EndsAt,
                result.RequiredSeconds,
                result.UsedSeconds,
                result.UsagePercentage,
                result.IsCompliant)
            {
                IsPrivate = meeting.IsPrivate
            };
        }).ToList();
    }

    /// <summary>One meeting of either kind, in the shape the compliance calculation needs.</summary>
    private sealed record ComplianceMeeting(Guid EventId, string Title, bool IsPrivate, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Provider);
}
