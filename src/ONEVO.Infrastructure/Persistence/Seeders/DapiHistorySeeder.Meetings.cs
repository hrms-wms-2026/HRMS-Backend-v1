using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using MeetingParticipant = ONEVO.Domain.Features.Calendar.Entities.CalendarEventParticipant;
using MeetingParticipantStatuses = ONEVO.Domain.Features.Calendar.Entities.CalendarEventParticipantStatuses;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiHistorySeeder
{
    private const string WeekdaysRule = "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR";
    private const string MondaysRule = "FREQ=WEEKLY;BYDAY=MO";

    /// <summary>Which demo project each team's stand-up lives in.</summary>
    private static readonly Dictionary<string, string> ProjectKeyByDept = new()
    {
        ["EPOS"] = "epos",
        ["EVTIX"] = "evtix",
        ["ONEXSO"] = "onexso",
        ["WCRAFT"] = "watercraft",
        ["HWINT"] = "hwportal",
        ["MKT"] = "onexso",
    };

    private sealed record MeetingPlan(
        string Key, string Title, string ProjectKey, Guid OrganizerEmployeeId, IReadOnlyList<Guid> AttendeeEmployeeIds,
        int Hour, int Minute, int DurationMinutes, string Rule);

    /// <summary>
    /// A daily stand-up per team (lead organizes, the team attends) and the owner's Monday leadership sync with the GM,
    /// HR and every team lead. Occurrences are materialized through the app's own writer for every slot from the window
    /// start to a week ahead, so the planner, Team Schedule and calendars read them like meetings created in the app.
    /// </summary>
    private static async Task SeedMeetingsAsync(SeedContext ctx, CancellationToken ct)
    {
        if (ctx.Services is null)
            return;
        var db = ctx.Db;
        var writer = ctx.Services.GetRequiredService<IMeetingOccurrenceWriter>();
        var materializer = ctx.Services.GetRequiredService<IMeetingOccurrenceMaterializer>();
        var recurrence = ctx.Services.GetRequiredService<IMeetingRecurrence>();
        var meetings = ctx.Services.GetRequiredService<IMeetingRepository>();
        var unitOfWork = ctx.Services.GetRequiredService<IUnitOfWork>();

        var active = ctx.Employees.Select(e => e.Id).ToHashSet();
        Guid Person(string key) => WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-demo:employee:{key}");
        Guid Hire(string key) => WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-org:employee:{key}");

        var plans = new List<MeetingPlan>();
        foreach (var team in DapiOrgStructureData.TeamGroups)
        {
            var lead = Person(team.LeadPersonKey);
            var members = team.MemberPersonKeys.Select(Person).Where(active.Contains).ToList();
            if (!active.Contains(lead) || members.Count == 0)
                continue;
            plans.Add(new MeetingPlan($"standup:{team.DeptCode}", "Daily stand-up", ProjectKeyByDept[team.DeptCode],
                lead, members, 9, 15, 15, WeekdaysRule));
        }
        var leadership = new[] { Hire("gm"), Hire("hrmgr") }
            .Concat(DapiOrgStructureData.TeamGroups.Select(t => Person(t.LeadPersonKey)))
            .Where(active.Contains)
            .ToList();
        if (leadership.Count > 0)
            plans.Add(new MeetingPlan("leadership-sync", "Weekly leadership sync", OwnerProjectKey, ctx.Owner.Id, leadership,
                10, 0, 60, MondaysRule));

        var from = At(ctx.Window.From, 0, 0);
        var to = At(ctx.Window.Today.AddDays(8), 0, 0);
        foreach (var plan in plans)
        {
            var series = await EnsureSeriesAsync(ctx, plan, ct);
            if (series is null)
                continue;
            var organizerUserId = series.CreatedById;
            var slots = recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, from, to);
            await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                await writer.LockSeriesAsync(DapiTenantId, series.Id, innerCt);
                foreach (var slot in slots)
                {
                    if (await meetings.GetDetailsAsync(DapiTenantId, series.Id, slot, innerCt) is not null)
                        continue;
                    await materializer.EnsureAsync(DapiTenantId, series, slot, organizerUserId, innerCt);
                }
                return true;
            }, ct);
        }
        db.ChangeTracker.Clear();
    }

    private static async Task<MeetingSeries?> EnsureSeriesAsync(SeedContext ctx, MeetingPlan plan, CancellationToken ct)
    {
        var db = ctx.Db;
        var seriesId = SeedId($"meeting-series:{plan.Key}");
        var existing = await db.MeetingSeries.FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (existing is not null)
            return existing.CancelledAt is null ? existing : null;

        var projectId = WorkManagementDapiDemoSeeder.DeterministicGuid($"dapi-demo:project:{plan.ProjectKey}");
        var rootObjectiveId = await db.Objectives
            .Where(o => o.TenantId == DapiTenantId && o.ProjectId == projectId && o.ParentObjectiveId == null)
            .OrderBy(o => o.CreatedAt)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(ct);
        if (rootObjectiveId is null)
            return null;

        var organizerUserId = ctx.Employees.First(e => e.Id == plan.OrganizerEmployeeId).UserId;
        var firstDay = ctx.Window.From;
        while (!IsWorkday(firstDay) || (plan.Rule == MondaysRule && firstDay.DayOfWeek != DayOfWeek.Monday))
            firstDay = firstDay.AddDays(1);
        var createdAt = At(firstDay.AddDays(-3), 11, 0);

        var series = new MeetingSeries
        {
            Id = seriesId,
            TenantId = DapiTenantId,
            ProjectId = projectId,
            ObjectiveId = rootObjectiveId.Value,
            OrganizerEmployeeId = plan.OrganizerEmployeeId,
            Title = plan.Title,
            Description = plan.Rule == MondaysRule
                ? "Week ahead: priorities, risks, hiring and anything that needs a decision."
                : "Yesterday, today, blockers. Keep it to fifteen minutes.",
            Location = "Teams",
            IsPrivate = false,
            DtStart = At(firstDay, plan.Hour, plan.Minute),
            DurationMinutes = plan.DurationMinutes,
            IsAllDay = false,
            Timezone = ColomboZoneId,
            RecurrenceRule = plan.Rule,
            CreationPath = MeetingCreationPaths.Direct,
            CreatedAt = createdAt,
            CreatedById = organizerUserId,
        };
        db.MeetingSeries.Add(series);
        foreach (var employeeId in plan.AttendeeEmployeeIds.Prepend(plan.OrganizerEmployeeId).Distinct())
        {
            db.Set<MeetingParticipant>().Add(new MeetingParticipant
            {
                Id = SeedId($"meeting-participant:{plan.Key}:{employeeId:N}"),
                TenantId = DapiTenantId,
                MeetingSeriesId = seriesId,
                EmployeeId = employeeId,
                ResponseStatus = MeetingParticipantStatuses.Accepted,
                RespondedAt = createdAt.AddHours(employeeId == plan.OrganizerEmployeeId ? 0 : 2),
                CreatedAt = createdAt,
                CreatedById = organizerUserId,
            });
        }
        await db.SaveChangesAsync(ct);
        return series;
    }
}
