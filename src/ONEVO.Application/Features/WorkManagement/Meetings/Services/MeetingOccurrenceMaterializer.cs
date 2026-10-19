using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Projects.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// Turns a series slot into a real occurrence from the series template: a meeting task ({Identifier}-M{n}, Meeting
/// category, placeholder status) plus its meeting_details row, through <see cref="IMeetingOccurrenceWriter"/>.
/// The caller holds the series lock and has re-read the series (spec 5.6 contract).
/// </summary>
public interface IMeetingOccurrenceMaterializer
{
    /// <summary>Materializes the slot as-is (no rule check); the task id, or null when it already exists or is suppressed.</summary>
    Task<Guid?> MaterializeAsync(Guid tenantId, MeetingSeries series, DateTimeOffset originalStart, Guid createdByUserId, CancellationToken ct);

    /// <summary>The occurrence's task id: the existing one, or a new one when the series' rule fires there and no
    /// suppression covers it; 404 meeting_not_found otherwise (also for a soft-deleted occurrence).</summary>
    Task<Result<Guid>> EnsureAsync(Guid tenantId, MeetingSeries series, DateTimeOffset originalStart, Guid createdByUserId, CancellationToken ct);
}

public sealed class MeetingOccurrenceMaterializer : IMeetingOccurrenceMaterializer
{
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingOccurrenceWriter _writer;
    private readonly IMeetingTaskDefaults _defaults;
    private readonly IProjectRepository _projects;
    private readonly IMeetingRecurrence _recurrence;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMeetingLinkRepository _links;

    public MeetingOccurrenceMaterializer(IMeetingRepository meetings, IMeetingOccurrenceWriter writer, IMeetingTaskDefaults defaults,
        IProjectRepository projects, IMeetingRecurrence recurrence, IUnitOfWork unitOfWork, IMeetingLinkRepository links)
    {
        _links = links;
        _unitOfWork = unitOfWork;
        _meetings = meetings;
        _writer = writer;
        _defaults = defaults;
        _projects = projects;
        _recurrence = recurrence;
    }

    public async Task<Guid?> MaterializeAsync(Guid tenantId, MeetingSeries series, DateTimeOffset originalStart, Guid createdByUserId, CancellationToken ct)
    {
        var project = await _projects.GetByIdForTenantAsync(tenantId, series.ProjectId, ct)
            ?? throw new InvalidOperationException($"Meeting series {series.Id} points at a missing project.");
        var defaults = await _defaults.ResolveAsync(tenantId, project.Id, createdByUserId, ct);
        if (!defaults.IsSuccess)
            throw new InvalidOperationException($"Meeting series {series.Id}: {defaults.Error}");
        var (categoryId, statusId) = defaults.Value;
        var start = originalStart.ToUniversalTime();
        var number = await _projects.IncrementAndGetNextMeetingNumberAsync(tenantId, project.Id, ct);
        // The insert is raw SQL: rows this unit of work added (a Meeting category for an older project, a new series)
        // must be in the database first. Still inside the caller's transaction.
        await _unitOfWork.SaveChangesAsync(ct);
        // Every occurrence uses the series' link (spec 4.2).
        var linkId = (await _links.GetActiveForSeriesAsync(tenantId, series.Id, ct))?.Id;

        return await _writer.TryMaterializeAsync(new MeetingOccurrenceDraft(
            tenantId, series.Id, series.ProjectId, series.ObjectiveId, categoryId, statusId,
            $"{project.Identifier}-M{number}", series.Title, series.Description, createdByUserId,
            start, start, start.AddMinutes(series.DurationMinutes), series.IsAllDay, series.Location, series.IsPrivate,
            CalendarEventMeetingId: linkId), ct);
    }

    public async Task<Result<Guid>> EnsureAsync(Guid tenantId, MeetingSeries series, DateTimeOffset originalStart, Guid createdByUserId, CancellationToken ct)
    {
        var key = originalStart.ToUniversalTime();
        if (await _meetings.GetDetailsAsync(tenantId, series.Id, key, ct) is { } existing)
            return await _meetings.IsOccurrenceRemovedAsync(tenantId, existing.TaskId, ct)
                ? MeetingReadService.NotFound<Guid>()
                : Result<Guid>.Success(existing.TaskId);

        var fires = _recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, key, key.AddSeconds(1)).Contains(key);
        if (!fires || await _meetings.IsSuppressedAsync(tenantId, series.Id, key, ct))
            return MeetingReadService.NotFound<Guid>();

        if (await MaterializeAsync(tenantId, series, key, createdByUserId, ct) is Guid created)
            return Result<Guid>.Success(created);
        // Lost to another writer between the check and the insert: it exists now.
        return await _meetings.GetDetailsAsync(tenantId, series.Id, key, ct) is { } raced
            ? Result<Guid>.Success(raced.TaskId)
            : MeetingReadService.NotFound<Guid>();
    }
}
