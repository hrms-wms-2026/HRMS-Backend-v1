using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Tests.Integration.MyTeam;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>A project placement (organizer, project, root module, a status) to hang meetings on.</summary>
public sealed record MeetingPlacement(Guid OrganizerEmployeeId, Guid ProjectId, Guid ObjectiveId, Guid StatusId);

/// <summary>Seeding helpers for meetings-as-tasks Phase 2: a series and its materialized occurrences, valid
/// against the database's pairing and placement triggers.</summary>
public static class MeetingSeed
{
    public static MeetingPlacement AddPlacement(this MyTeamDb h, ApplicationDbContext db)
    {
        var organizer = h.AddEmployee(db);
        var (projectId, rootId) = h.AddProject(db, organizer.Id);
        var statusId = h.AddStatus(db, projectId, marksComplete: false);
        return new MeetingPlacement(organizer.Id, projectId, rootId, statusId);
    }

    public static MeetingSeries AddSeries(this MyTeamDb h, ApplicationDbContext db, MeetingPlacement at,
        DateTimeOffset? dtStart = null, string? recurrenceRule = null)
    {
        var series = new MeetingSeries
        {
            Id = Guid.NewGuid(), TenantId = h.TenantId, ProjectId = at.ProjectId, ObjectiveId = at.ObjectiveId,
            OrganizerEmployeeId = at.OrganizerEmployeeId, Title = $"Meeting {Guid.NewGuid():N}"[..16],
            DtStart = dtStart ?? new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero), DurationMinutes = 30,
            Timezone = "UTC", RecurrenceRule = recurrenceRule, CreationPath = MeetingCreationPaths.Direct,
            CreatedById = at.OrganizerEmployeeId,
        };
        db.MeetingSeries.Add(series);
        return series;
    }

    /// <summary>A materialized occurrence: a meeting task at the series' placement plus its meeting_details row.</summary>
    public static (WorkTask Task, MeetingDetails Details) AddOccurrence(this MyTeamDb h, ApplicationDbContext db,
        MeetingSeries series, MeetingPlacement at, DateTimeOffset? originalStart = null)
    {
        var start = originalStart ?? series.DtStart;
        var task = h.AddTask(db, series.ProjectId, series.ObjectiveId, at.StatusId, progress: 0, due: null);
        task.TaskKind = WorkTaskKinds.Meeting;
        task.Title = series.Title;
        var details = new MeetingDetails
        {
            TaskId = task.Id, TenantId = h.TenantId, SeriesId = series.Id, OriginalStart = start,
            StartAt = start, EndAt = start.AddMinutes(series.DurationMinutes),
        };
        db.MeetingDetails.Add(details);
        return (task, details);
    }
}
