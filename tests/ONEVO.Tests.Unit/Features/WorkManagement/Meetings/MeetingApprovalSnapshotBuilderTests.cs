using System.Text.Json;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>D5: the snapshot a meeting.move approver sees, frozen when the request is submitted.</summary>
public class MeetingApprovalSnapshotBuilderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ToProject = Guid.NewGuid();
    private static readonly Guid Priya = Guid.NewGuid();
    private static readonly Guid Arjun = Guid.NewGuid();
    private static readonly DateTimeOffset OriginalStart = new(2026, 11, 2, 3, 30, 0, TimeSpan.Zero);

    private readonly Objective _root = new() { Id = Guid.NewGuid(), ProjectId = ToProject, Title = "Apollo", IsDefault = true };
    private readonly Objective _delivery;
    private readonly Objective _qa;
    private readonly MeetingSeries _series;

    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IMeetingRepository> _meetings = new();
    private readonly Mock<IMeetingLinkRepository> _links = new();

    public MeetingApprovalSnapshotBuilderTests()
    {
        _delivery = new Objective { Id = Guid.NewGuid(), ProjectId = ToProject, ParentObjectiveId = _root.Id, Title = "Delivery" };
        _qa = new Objective { Id = Guid.NewGuid(), ProjectId = ToProject, ParentObjectiveId = _delivery.Id, Title = "QA" };
        _series = new MeetingSeries
        {
            Id = Guid.NewGuid(), TenantId = TenantId, OrganizerEmployeeId = Priya, Title = "Weekly sync", Description = "<p>Agenda</p>",
            DtStart = OriginalStart, DurationMinutes = 30, Timezone = "Asia/Colombo", RecurrenceRule = "FREQ=WEEKLY;BYDAY=MO",
        };

        _hierarchy.Setup(h => h.LoadTreeAsync(TenantId, ToProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[] { _root, _delivery, _qa }));
        _employees.Setup(e => e.ListByIdsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee>
            {
                [Priya] = new() { Id = Priya, FirstName = "Priya", LastName = "K" },
                [Arjun] = new() { Id = Arjun, FirstName = "Arjun", LastName = "M" },
            });
        _meetings.Setup(m => m.ListSeriesParticipantsAsync(TenantId, _series.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEventParticipant>
            {
                new() { MeetingSeriesId = _series.Id, EmployeeId = Priya },
                new() { MeetingSeriesId = _series.Id, EmployeeId = Arjun },
            });
        _meetings.Setup(m => m.ListSeriesGuestsAsync(TenantId, _series.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CalendarEventGuest> { new() { MeetingSeriesId = _series.Id, Email = "client@example.com" } });
        _links.Setup(l => l.GetActiveForSeriesAsync(TenantId, _series.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarEventMeeting { Provider = CalendarEventMeetingProviders.Zoom });
    }

    private MeetingApprovalSnapshotBuilder Build() => new(_employees.Object, _hierarchy.Object, _meetings.Object, _links.Object);

    [Fact]
    public async Task ForMove_freezes_the_D5_fields_with_the_destination_path()
    {
        var snapshot = await Build().ForMoveAsync(TenantId, _series, OriginalStart, MeetingScopes.Following, ToProject, _qa.Id, default);

        Assert.Equal("Weekly sync", snapshot.Title);
        Assert.Equal("<p>Agenda</p>", snapshot.Description);
        Assert.Equal(OriginalStart, snapshot.Start);
        Assert.Equal(30, snapshot.DurationMinutes);
        Assert.Equal("Asia/Colombo", snapshot.Timezone);
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO", snapshot.RecurrenceSummary);
        Assert.Equal("Priya K", snapshot.OrganizerName);
        Assert.Equal("Apollo / Delivery / QA", snapshot.ModulePath);
        Assert.Equal(["Arjun M"], snapshot.ParticipantNames);
        Assert.Equal(["client@example.com"], snapshot.GuestEmails);
        Assert.Equal("zoom", snapshot.Platform);
    }

    [Fact]
    public async Task ForMove_this_scope_says_this_occurrence_only()
    {
        // A detached occurrence: its own time and title.
        var taskId = Guid.NewGuid();
        var movedStart = OriginalStart.AddHours(2);
        _meetings.Setup(m => m.GetDetailsAsync(TenantId, _series.Id, OriginalStart, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MeetingDetails { TaskId = taskId, SeriesId = _series.Id, OriginalStart = OriginalStart, StartAt = movedStart,
                EndAt = movedStart.AddMinutes(45), IsDetached = true });
        _meetings.Setup(m => m.GetMeetingTaskTrackedAsync(TenantId, taskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkTask { Id = taskId, Title = "Weekly sync (moved)", Description = "<p>Other</p>" });

        var snapshot = await Build().ForMoveAsync(TenantId, _series, OriginalStart, MeetingScopes.This, ToProject, _qa.Id, default);

        Assert.Equal("This occurrence only", snapshot.RecurrenceSummary);
        Assert.Equal(movedStart, snapshot.Start);
        Assert.Equal(45, snapshot.DurationMinutes);
        Assert.Equal("Weekly sync (moved)", snapshot.Title);
        Assert.Equal("<p>Other</p>", snapshot.Description);
    }

    [Fact]
    public void MovePayload_without_snapshot_still_deserializes()
    {
        var series = Guid.NewGuid();
        var project = Guid.NewGuid();
        var module = Guid.NewGuid();
        var json = $$"""{"seriesId":"{{series}}","originalStart":"2026-11-02T03:30:00Z","scope":"this","projectId":"{{project}}","objectiveId":"{{module}}"}""";

        var payload = JsonSerializer.Deserialize<MeetingMovePayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Null(payload.Snapshot);
        Assert.Equal(series, payload.SeriesId);
        Assert.Equal(OriginalStart, payload.OriginalStart);
        Assert.Equal("this", payload.Scope);
        Assert.Equal(project, payload.ProjectId);
        Assert.Equal(module, payload.ObjectiveId);
    }
}
