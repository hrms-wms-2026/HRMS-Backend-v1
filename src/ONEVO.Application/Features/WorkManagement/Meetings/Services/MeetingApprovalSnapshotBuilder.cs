using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>The D5 approver view of a meeting.create / meeting.move request, frozen when it is submitted: names and paths
/// resolved now, so later renames never change what was approved. Never the link URL, attendance or action items.</summary>
public interface IMeetingApprovalSnapshotBuilder
{
    Task<MeetingApprovalSnapshot> ForCreateAsync(Guid tenantId, Guid organizerEmployeeId, MeetingPlacement at,
        CreateMeetingRequest request, CancellationToken ct);

    /// <summary>The occurrence being moved (its own time and title when it was edited) and the destination module path.</summary>
    Task<MeetingApprovalSnapshot> ForMoveAsync(Guid tenantId, MeetingSeries series, DateTimeOffset originalStart, string scope,
        Guid toProjectId, Guid toObjectiveId, CancellationToken ct);
}

public sealed class MeetingApprovalSnapshotBuilder : IMeetingApprovalSnapshotBuilder
{
    private const string ThisOccurrenceOnly = "This occurrence only";
    private const string DoesNotRepeat = "Does not repeat";

    private readonly IEmployeeRepository _employees;
    private readonly IWorkHierarchyService _hierarchy;
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingLinkRepository _links;

    public MeetingApprovalSnapshotBuilder(IEmployeeRepository employees, IWorkHierarchyService hierarchy, IMeetingRepository meetings,
        IMeetingLinkRepository links)
    {
        _employees = employees;
        _hierarchy = hierarchy;
        _meetings = meetings;
        _links = links;
    }

    public async Task<MeetingApprovalSnapshot> ForCreateAsync(Guid tenantId, Guid organizerEmployeeId, MeetingPlacement at,
        CreateMeetingRequest request, CancellationToken ct)
    {
        var people = await _employees.ListByIdsAsync(tenantId,
            request.ParticipantEmployeeIds.Append(organizerEmployeeId).Distinct().ToList(), ct);
        string Name(Guid id) => NameOf(people, id);

        return new MeetingApprovalSnapshot(request.Title.Trim(), request.Description?.Trim(), request.Start, request.DurationMinutes,
            request.Timezone, request.RecurrenceRule ?? DoesNotRepeat, Name(organizerEmployeeId),
            await ModulePathAsync(tenantId, at.ProjectId, at.ObjectiveId, ct),
            request.ParticipantEmployeeIds.Where(id => id != organizerEmployeeId).Distinct().Select(Name).ToList(),
            MeetingCreateValidator.NormalizeGuests(request.GuestEmails), request.Platform);
    }

    public async Task<MeetingApprovalSnapshot> ForMoveAsync(Guid tenantId, MeetingSeries series, DateTimeOffset originalStart,
        string scope, Guid toProjectId, Guid toObjectiveId, CancellationToken ct)
    {
        var details = await _meetings.GetDetailsAsync(tenantId, series.Id, originalStart, ct);
        var task = details is null ? null : await _meetings.GetMeetingTaskTrackedAsync(tenantId, details.TaskId, ct);
        var participants = (await _meetings.ListSeriesParticipantsAsync(tenantId, series.Id, ct))
            .Select(p => p.EmployeeId).Where(id => id != series.OrganizerEmployeeId).Distinct().ToList();
        var people = await _employees.ListByIdsAsync(tenantId, participants.Append(series.OrganizerEmployeeId).ToList(), ct);
        var guests = await _meetings.ListSeriesGuestsAsync(tenantId, series.Id, ct);
        var link = await _links.GetActiveForSeriesAsync(tenantId, series.Id, ct);

        return new MeetingApprovalSnapshot(
            task?.Title ?? series.Title,
            task is null ? series.Description : task.Description,
            details?.StartAt ?? originalStart,
            details is null ? series.DurationMinutes : (int)(details.EndAt - details.StartAt).TotalMinutes,
            series.Timezone,
            scope == MeetingScopes.This ? ThisOccurrenceOnly : series.RecurrenceRule ?? DoesNotRepeat,
            NameOf(people, series.OrganizerEmployeeId),
            await ModulePathAsync(tenantId, toProjectId, toObjectiveId, ct),
            participants.Select(id => NameOf(people, id)).OrderBy(n => n, StringComparer.CurrentCulture).ToList(),
            guests.Select(g => g.Email).ToList(),
            link?.Provider);
    }

    private async Task<string> ModulePathAsync(Guid tenantId, Guid projectId, Guid objectiveId, CancellationToken ct)
    {
        var tree = await _hierarchy.LoadTreeAsync(tenantId, projectId, ct);
        return string.Join(" / ", tree.AncestorChain(objectiveId).Reverse().Select(m => m.Title));
    }

    private static string NameOf(IReadOnlyDictionary<Guid, Domain.Features.CoreHr.Entities.Employee> people, Guid id)
        => people.TryGetValue(id, out var e) ? $"{e.FirstName} {e.LastName}".Trim() : "Unknown";
}
