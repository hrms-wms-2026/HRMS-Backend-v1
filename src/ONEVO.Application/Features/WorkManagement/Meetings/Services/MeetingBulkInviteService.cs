using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.OrgStructure.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// The pure part of D8: the picked groups and the picked people, flattened into one snapshot with the four counts
/// the organizer sees. No I/O - the service feeds it the rows it has already read.
/// </summary>
public static class MeetingBulkExpansion
{
    /// <summary>
    /// Flattens <paramref name="explicitIds"/> and every group into one list of people.
    /// <list type="bullet">
    /// <item><see cref="People"/>: the active people, minus those already invited, in first-seen order.</item>
    /// <item><see cref="Selected"/>: every id before dedup - what the organizer picked.</item>
    /// <item><see cref="Duplicates"/>: the repeats.</item>
    /// <item><see cref="Inactive"/>: the distinct ids that are not active (they include ids with no employee row).</item>
    /// <item><see cref="AlreadyInvited"/>: the distinct active ids that are already on the meeting.</item>
    /// </list>
    /// </summary>
    public static (IReadOnlyList<Guid> People, int Selected, int Duplicates, int Inactive, int AlreadyInvited) Expand(
        IReadOnlyList<Guid> explicitIds,
        IReadOnlyList<IReadOnlyList<Guid>> groups,
        IReadOnlySet<Guid> active,
        IReadOnlySet<Guid> alreadyInvited)
    {
        var all = new List<Guid>(explicitIds.Count);
        all.AddRange(explicitIds);
        foreach (var group in groups)
            all.AddRange(group);

        var distinct = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var id in all)
            if (seen.Add(id))
                distinct.Add(id);

        var people = new List<Guid>();
        var inactive = 0;
        var already = 0;
        foreach (var id in distinct)
        {
            if (!active.Contains(id))
            {
                inactive++;
                continue;
            }
            if (alreadyInvited.Contains(id))
            {
                already++;
                continue;
            }
            people.Add(id);
        }

        return (people, all.Count, all.Count - distinct.Count, inactive, already);
    }
}

/// <summary>
/// D8: the bulk invite preview. The organizer picks a whole department or a manager's direct reports; the preview
/// expands those groups into a snapshot of individual people, with the counts (selected, duplicates removed,
/// inactive excluded, already invited) and, above the provider's capacity, a warning - never a cap.
/// </summary>
public sealed class MeetingBulkInviteService
{
    /// <summary>The same "active" the rest of the Meetings feature uses: working, or on leave.</summary>
    private static readonly HashSet<int> ActiveStatuses = [EmploymentStatusIds.Active, EmploymentStatusIds.OnLeave];

    private readonly IOptions<MeetingsOptions> _options;
    private readonly IEmployeeRepository _employees;
    private readonly IDepartmentRepository _departments;
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingPlacementResolver _placements;
    private readonly IMeetingAccessResolver _access;

    public MeetingBulkInviteService(
        IOptions<MeetingsOptions> options,
        IEmployeeRepository employees,
        IDepartmentRepository departments,
        IMeetingRepository meetings,
        IMeetingPlacementResolver placements,
        IMeetingAccessResolver access)
    {
        _options = options;
        _employees = employees;
        _departments = departments;
        _meetings = meetings;
        _placements = placements;
        _access = access;
    }

    public async Task<Result<MeetingBulkPreviewResponse>> PreviewAsync(
        Guid tenantId, Guid callerEmployeeId, MeetingBulkPreviewRequest request, CancellationToken ct)
    {
        var options = _options.Value;
        if (!options.BulkInvite)
            return Result<MeetingBulkPreviewResponse>.Failure(
                "Bulk inviting is not available yet.", 409, MeetingErrorCodes.BulkInviteUnavailable);

        if (request.Platform is { } platform && !CalendarMeetingConnections.IsKnownProvider(platform))
            return Result<MeetingBulkPreviewResponse>.Failure(
                "Choose Microsoft Teams or Zoom.", 400, MeetingErrorCodes.PlatformInvalid);

        var alreadyInvited = new HashSet<Guid> { callerEmployeeId };
        if (request.SeriesId is Guid seriesId)
        {
            // R2: the series decides who may preview. Someone who cannot see the series gets the one 404 body;
            // an attendee gets 403 - the preview adds attendees, so only the organizer may use it.
            var access = await _access.ResolveForSeriesAsync(tenantId, callerEmployeeId, seriesId, ct);
            if (access is null)
                return MeetingReadService.NotFound<MeetingBulkPreviewResponse>();
            if (access.Role != MeetingRoles.Organizer)
                return Result<MeetingBulkPreviewResponse>.Failure(
                    "Only the organizer can change this meeting.", 403, MeetingErrorCodes.OrganizerRequired);

            foreach (var participant in await _meetings.ListSeriesParticipantsAsync(tenantId, seriesId, ct))
                if (!participant.IsDeleted)
                    alreadyInvited.Add(participant.EmployeeId);
        }
        else
        {
            // The create gate: the same rule the meeting itself goes through.
            var placement = await _placements.ResolveAsync(tenantId, callerEmployeeId, request.ProjectId, request.ObjectiveId, ct);
            if (!placement.IsSuccess)
                return Result<MeetingBulkPreviewResponse>.Failure(
                    placement.Error!, placement.StatusCode ?? 400, placement.ErrorCode);
        }

        var groups = new List<IReadOnlyList<Guid>>();
        if (request.DepartmentIds.Count > 0)
        {
            var departmentMembers = await _employees.ListByDepartmentIdsAsync(tenantId, request.DepartmentIds, ct);
            groups.Add(departmentMembers.Select(e => e.Id).ToList());
        }
        foreach (var managerEmployeeId in request.ManagerEmployeeIds.Distinct())
            groups.Add(await _employees.ListDirectReportEmployeeIdsAsync(tenantId, managerEmployeeId, ct));

        var candidates = request.EmployeeIds
            .Concat(groups.SelectMany(g => g))
            .Distinct()
            .ToList();
        var people = await _employees.ListByIdsAsync(tenantId, candidates, ct);
        var active = people.Values
            .Where(e => ActiveStatuses.Contains(e.EmploymentStatusId))
            .Select(e => e.Id)
            .ToHashSet();

        var expansion = MeetingBulkExpansion.Expand(request.EmployeeIds, groups, active, alreadyInvited);

        var departmentIds = expansion.People
            .Where(id => people.TryGetValue(id, out var employee) && employee.DepartmentId is not null)
            .Select(id => people[id].DepartmentId!.Value)
            .Distinct()
            .ToList();
        var departmentNames = await _departments.GetByIdsAsync(tenantId, departmentIds, ct);

        var rows = expansion.People
            .Where(id => people.ContainsKey(id))
            .Select(id => people[id])
            .Select(e => new MeetingBulkPerson(
                e.Id,
                $"{e.FirstName} {e.LastName}".Trim(),
                string.IsNullOrWhiteSpace(e.Email) ? null : e.Email,
                e.DepartmentId is Guid departmentId && departmentNames.TryGetValue(departmentId, out var department) ? department.Name : null))
            .ToList();

        // The meeting's size is the people being invited plus the ones already on it (the organizer, and the
        // series' participants) - not the picked-and-duplicated count the response reports.
        var capacityWarning = CapacityWarning(options, request.Platform, rows.Count, alreadyInvited.Count);

        return Result<MeetingBulkPreviewResponse>.Success(new MeetingBulkPreviewResponse(
            rows, expansion.Selected, expansion.Duplicates, expansion.Inactive, expansion.AlreadyInvited, capacityWarning));
    }

    /// <summary>A warning, never a cap (spec 5.2): above the provider's capacity the organizer is told, and may go ahead.</summary>
    private static string? CapacityWarning(MeetingsOptions options, string? platform, int people, int alreadyInvited)
    {
        if (platform is null)
            return null;
        // The configuration is keyed by the provider's configuration key ("MicrosoftTeams", "Zoom"); the request
        // carries the provider constant ("microsoft_teams", "zoom"). Both spellings are accepted.
        var configKey = MeetingProviderCapacityKeys.For(platform);
        var limit = options.ProviderCapacityWarning
            .Where(pair => string.Equals(pair.Key, configKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pair.Key, platform, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value)
            .FirstOrDefault();
        if (limit <= 0)
            return null;

        var total = people + alreadyInvited;
        return total > limit
            ? $"This meeting will have {total} people; {CalendarMeetingProviderNames.Display(platform)} meetings support up to {limit}."
            : null;
    }
}
