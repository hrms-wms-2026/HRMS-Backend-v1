using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using Microsoft.Extensions.Options;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// The action items of a meeting occurrence (spec 5.4, D14). An action item is an ordinary task; this service
/// owns only the link to the occurrence it was raised from:
/// - the source of a create is validated here (the caller must be able to see the occurrence, and it must
///   still exist and not be cancelled), so an invisible or gone source is a 404 and nothing is created;
/// - the link is written with the task (direct create) or when the approval is applied;
/// - the list shows only the linked tasks the caller may see under the ordinary task rules;
/// - only the link's creator or the meeting's organizer may unlink - the task itself always stays.
/// </summary>
public sealed class MeetingActionItemService
{
    private readonly IOptions<MeetingsOptions> _options;
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingAccessResolver _access;
    private readonly IMeetingActionItemRepository _links;
    private readonly IWorkTaskRepository _tasks;
    private readonly ITaskStatusRepository _statuses;
    private readonly ITaskAccessResolver _taskAccess;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public MeetingActionItemService(
        IOptions<MeetingsOptions> options, IMeetingRepository meetings, IMeetingAccessResolver access,
        IMeetingActionItemRepository links, IWorkTaskRepository tasks, ITaskStatusRepository statuses,
        ITaskAccessResolver taskAccess, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _options = options;
        _meetings = meetings;
        _access = access;
        _links = links;
        _tasks = tasks;
        _statuses = statuses;
        _taskAccess = taskAccess;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    /// <summary>
    /// The occurrence a task is being raised from: 404 <c>meeting_not_found</c> unless the caller can see it,
    /// it is materialized, it was not removed and it is not cancelled.
    /// </summary>
    public async Task<Result<bool>> ValidateSourceAsync(
        Guid tenantId, Guid callerEmployeeId, Guid occurrenceTaskId, CancellationToken ct = default)
    {
        var row = await _meetings.GetOccurrenceByTaskAsync(tenantId, occurrenceTaskId, ct);
        if (row is null)
            return MeetingReadService.NotFound<bool>();
        if (row.Details.CancelledAt is not null)
            return MeetingReadService.NotFound<bool>();
        if (await _meetings.IsOccurrenceRemovedAsync(tenantId, occurrenceTaskId, ct))
            return MeetingReadService.NotFound<bool>();
        var access = await _access.ResolveForOccurrenceAsync(tenantId, callerEmployeeId, row.Details.SeriesId, row.Details.OriginalStart, ct);
        return access is null
            ? MeetingReadService.NotFound<bool>()
            : Result<bool>.Success(true);
    }

    /// <summary>
    /// Links a task to the occurrence it was raised from. Runs inside the caller's transaction and is a no-op
    /// when the occurrence is gone or cancelled, or when the task already has a link (one link per task, spec 4.1).
    /// </summary>
    public async Task LinkAsync(
        Guid tenantId, Guid occurrenceTaskId, Guid taskId, Guid creatorEmployeeId, CancellationToken ct = default)
    {
        var row = await _meetings.GetOccurrenceByTaskAsync(tenantId, occurrenceTaskId, ct);
        if (row is null || row.Details.CancelledAt is not null)
            return;
        if (await _meetings.IsOccurrenceRemovedAsync(tenantId, occurrenceTaskId, ct))
            return;
        if (await _links.ExistsForTaskAsync(tenantId, taskId, ct))
            return;

        _links.Add(new MeetingActionItem
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OccurrenceTaskId = occurrenceTaskId, TaskId = taskId,
            CreatedByEmployeeId = creatorEmployeeId, CreatedAt = _clock.UtcNow,
        });
    }

    /// <summary>
    /// The occurrence's action items: the linked tasks the caller may see under the ordinary task rules.
    /// The caller must be able to see the meeting itself (404 otherwise).
    /// </summary>
    public async Task<Result<IReadOnlyList<MeetingActionItemView>>> ListAsync(
        Guid tenantId, Guid callerEmployeeId, Guid callerUserId, Guid occurrenceTaskId, CancellationToken ct = default)
    {
        var row = await _meetings.GetOccurrenceByTaskAsync(tenantId, occurrenceTaskId, ct);
        if (row is null)
            return MeetingReadService.NotFound<IReadOnlyList<MeetingActionItemView>>();
        var access = await _access.ResolveForOccurrenceAsync(tenantId, callerEmployeeId, row.Details.SeriesId, row.Details.OriginalStart, ct);
        if (access is null)
            return MeetingReadService.NotFound<IReadOnlyList<MeetingActionItemView>>();

        var links = await _links.ListForOccurrenceAsync(tenantId, occurrenceTaskId, ct);
        if (links.Count == 0)
            return Result<IReadOnlyList<MeetingActionItemView>>.Success([]);

        // One query for all the linked tasks, not one per link.
        var tasks = await _tasks.GetByIdsForTenantAsync(tenantId, links.Select(l => l.TaskId).Distinct().ToList(), ct);
        if (tasks.Count == 0)
            return Result<IReadOnlyList<MeetingActionItemView>>.Success([]);

        // The task rules decide what the caller may see: the assignees-scope filter first, then the exact
        // per-task visibility check (a task in a module the caller is not a member of is not theirs to see).
        var viewable = await _taskAccess.FilterViewableTasksAsync(tenantId, callerUserId, callerEmployeeId, tasks, ct);
        var visible = await _taskAccess.ResolveViewableTasksAsync(tenantId, callerUserId, viewable, ct);
        if (visible.Count == 0)
            return Result<IReadOnlyList<MeetingActionItemView>>.Success([]);

        var statusIds = visible.Select(t => t.StatusId).Distinct().ToList();
        var statuses = (await _statuses.GetByIdsForTenantAsync(tenantId, statusIds, ct)).ToDictionary(s => s.Id);
        var assignees = await _tasks.GetAssignedEmployeeIdsByTaskIdsAsync(visible.Select(t => t.Id).ToList(), ct);
        var linksByTask = links.ToDictionary(l => l.TaskId);

        return Result<IReadOnlyList<MeetingActionItemView>>.Success(visible.Select(task =>
        {
            var link = linksByTask[task.Id];
            // An item with no assignment has no assignee - not an empty guid.
            var assignee = assignees.GetValueOrDefault(task.Id, Array.Empty<Guid>()).FirstOrDefault();
            return new MeetingActionItemView(
                task.Id, task.ShortId, task.Title, statuses.GetValueOrDefault(task.StatusId)?.Name ?? string.Empty,
                task.ObjectiveId, assignee == Guid.Empty ? null : assignee,
                link.CreatedByEmployeeId, link.CreatedAt);
        }).ToList());
    }

    /// <summary>
    /// Removes the link between an occurrence and a task. Only the link's creator or the meeting's organizer may
    /// do it (403 <c>meeting_organizer_required</c> for anyone else who can see the meeting). The task itself stays.
    /// </summary>
    public async Task<Result<bool>> UnlinkAsync(
        Guid tenantId, Guid callerEmployeeId, Guid occurrenceTaskId, Guid taskId, CancellationToken ct = default)
    {
        if (MeetingGuard.CheckWritable<bool>(_options.Value) is { } blocked)
            return blocked;

        var row = await _meetings.GetOccurrenceByTaskAsync(tenantId, occurrenceTaskId, ct);
        if (row is null)
            return MeetingReadService.NotFound<bool>();
        var access = await _access.ResolveForOccurrenceAsync(tenantId, callerEmployeeId, row.Details.SeriesId, row.Details.OriginalStart, ct);
        if (access is null)
            return MeetingReadService.NotFound<bool>();

        var link = await _links.GetTrackedAsync(tenantId, occurrenceTaskId, taskId, ct);
        if (link is null)
            return MeetingReadService.NotFound<bool>();

        if (link.CreatedByEmployeeId != callerEmployeeId && access.Role != MeetingRoles.Organizer)
            return Result<bool>.Failure("Only the organizer can change this meeting.", 403, MeetingErrorCodes.OrganizerRequired);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            _links.Remove(link);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<bool>.Success(true);
        }, ct);
    }
}
