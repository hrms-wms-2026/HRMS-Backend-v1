using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>What the series looks like from the split point on.</summary>
public sealed record SeriesTemplate(string Title, string? Description, string? Location, bool IsPrivate, DateTimeOffset DtStart,
    int DurationMinutes, bool IsAllDay, string Timezone, string? RecurrenceRule);

public interface IMeetingSeriesSplitter
{
    /// <summary>
    /// Spec 5.5 forward split at <paramref name="splitAt"/>, in the caller's transaction, with the caller holding the
    /// source series lock (and having re-read it). The source ends just before the point (its past is untouched); a new
    /// series carries the rest with the template, copied attendees (RSVPs reset when the time or length changes) and the
    /// mapped suppressions; materialized occurrences move across keeping their task ids (a rescheduled one keeps its own
    /// time, shifted with its slot); surplus ones are cancelled when they carry overrides, action items or attendance, else
    /// soft-deleted; missing ones in the 7-day window are created. Occurrences that have started are never touched.
    /// A <paramref name="newPlacement"/> (a move) re-places the moved tasks.
    /// </summary>
    Task<Result<MeetingSeries>> SplitAsync(Guid tenantId, MeetingSeries source, DateTimeOffset splitAt, string reason,
        SeriesTemplate template, MeetingPlacement? newPlacement, Guid actorEmployeeId, Guid actorUserId, CancellationToken ct);
}

public sealed class MeetingSeriesSplitter : IMeetingSeriesSplitter
{
    private static readonly TimeSpan ExpansionSlack = TimeSpan.FromDays(400);

    private readonly IMeetingRepository _meetings;
    private readonly IMeetingOccurrenceWriter _writer;
    private readonly IMeetingOccurrenceMaterializer _materializer;
    private readonly IMeetingRecurrence _recurrence;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public MeetingSeriesSplitter(IMeetingRepository meetings, IMeetingOccurrenceWriter writer, IMeetingOccurrenceMaterializer materializer,
        IMeetingRecurrence recurrence, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _meetings = meetings;
        _writer = writer;
        _materializer = materializer;
        _recurrence = recurrence;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<MeetingSeries>> SplitAsync(Guid tenantId, MeetingSeries source, DateTimeOffset splitAt, string reason,
        SeriesTemplate template, MeetingPlacement? newPlacement, Guid actorEmployeeId, Guid actorUserId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var point = splitAt.ToUniversalTime();
        var materialized = await _meetings.ListOccurrencesFromAsync(tenantId, source.Id, point, ct);
        var suppressed = await _meetings.ListSuppressionsFromAsync(tenantId, source.Id, point, ct);
        var horizon = new[] { now + MeetingSeriesFactory.Window }
            .Concat(materialized.Select(d => d.OriginalStart)).Concat(suppressed.Select(s => s.OriginalStart)).Max().AddSeconds(1);

        var oldExpansion = source.RecurrenceRule is null
            ? (source.DtStart >= point ? [source.DtStart] : [])
            : _recurrence.Occurrences(source.RecurrenceRule, source.DtStart, source.Timezone, point, horizon);
        var newExpansion = (template.RecurrenceRule is null
                ? [template.DtStart.ToUniversalTime()]
                : _recurrence.Occurrences(template.RecurrenceRule, template.DtStart, template.Timezone, template.DtStart,
                    template.DtStart + (horizon - point) + ExpansionSlack))
            .ToList();
        if (newExpansion.Count == 0)
            return Result<MeetingSeries>.Failure("The new repeat rule has no occurrences.", 409, MeetingErrorCodes.RecurrenceEmpty);
        var plan = MeetingSplitPlan.Create(oldExpansion, materialized.Select(d => d.OriginalStart), suppressed.Select(s => s.OriginalStart), newExpansion);
        // Forward-only: an occurrence that has already started (one rescheduled earlier than its slot) stays where it is,
        // untouched; its new slot is suppressed so the new series never schedules that meeting a second time.
        var started = materialized.Where(d => d.StartAt <= now).Select(d => d.OriginalStart).ToHashSet();
        var remaps = plan.Remaps.Where(r => !started.Contains(r.OldKey)).ToList();
        var keptStarted = plan.Remaps.Where(r => started.Contains(r.OldKey)).ToList();

        // 1. The old series ends just before the point.
        if (source.RecurrenceRule is not null)
            source.RecurrenceRule = MeetingRules.WithUntil(source.RecurrenceRule, point.AddSeconds(-1));
        source.Revision++;
        source.UpdatedAt = now;

        // 2. The new series, with the attendees.
        var target = new MeetingSeries
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            ProjectId = newPlacement?.ProjectId ?? source.ProjectId, ObjectiveId = newPlacement?.ObjectiveId ?? source.ObjectiveId,
            OrganizerEmployeeId = source.OrganizerEmployeeId, Title = template.Title, Description = template.Description,
            Location = template.Location, IsPrivate = template.IsPrivate, DtStart = template.DtStart.ToUniversalTime(),
            DurationMinutes = template.DurationMinutes, IsAllDay = template.IsAllDay, Timezone = template.Timezone,
            RecurrenceRule = template.RecurrenceRule, CreationPath = source.CreationPath, SplitFromSeriesId = source.Id,
            SplitReason = reason, SplitAt = point, CreatedAt = now, CreatedById = actorUserId,
        };
        _meetings.AddSeries(target);
        var resetRsvp = target.DtStart != point || template.DurationMinutes != source.DurationMinutes;
        _meetings.AddParticipants((await _meetings.ListSeriesParticipantsAsync(tenantId, source.Id, ct)).Where(p => !p.IsDeleted)
            .Select(p => new CalendarEventParticipant
            {
                Id = Guid.NewGuid(), TenantId = tenantId, MeetingSeriesId = target.Id, EmployeeId = p.EmployeeId,
                ResponseStatus = resetRsvp && p.EmployeeId != source.OrganizerEmployeeId ? CalendarEventParticipantStatuses.Pending : p.ResponseStatus,
                ResponseReason = resetRsvp ? null : p.ResponseReason,
                RespondedAt = resetRsvp && p.EmployeeId != source.OrganizerEmployeeId ? null : p.RespondedAt,
            }));
        _meetings.AddGuests((await _meetings.ListSeriesGuestsAsync(tenantId, source.Id, ct)).Select(g => new CalendarEventGuest
        {
            Id = Guid.NewGuid(), TenantId = tenantId, MeetingSeriesId = target.Id, Email = g.Email, CreatedAt = now,
        }));
        await _unitOfWork.SaveChangesAsync(ct);   // the remap and the occurrence inserts are raw SQL against the new series
        await _writer.LockSeriesAsync(tenantId, target.Id, ct);

        // 3. Materialized occurrences move across, keeping their task ids; non-detached ones take the new template. A
        //    rescheduled (detached) one keeps its own time and length, moved by as much as its slot moved.
        var byKey = materialized.ToDictionary(d => d.OriginalStart);
        var length = TimeSpan.FromMinutes(template.DurationMinutes);
        if (remaps.Count > 0)
            await _writer.RemapIntoSeriesAsync(tenantId, target.Id, remaps.Select(r =>
            {
                var old = byKey[r.OldKey];
                var moved = r.NewKey - r.OldKey;
                return old.IsDetached
                    ? (old.TaskId, r.NewKey, old.StartAt + moved, old.EndAt + moved)
                    : (old.TaskId, r.NewKey, r.NewKey, r.NewKey + length);
            }).ToList(), ct);
        foreach (var (oldKey, _) in remaps)
        {
            var old = byKey[oldKey];
            var task = (await _meetings.GetMeetingTaskTrackedAsync(tenantId, old.TaskId, ct))!;
            var details = (await _meetings.GetDetailsTrackedAsync(tenantId, old.TaskId, ct))!;
            if (!old.IsDetached)
            {
                task.Title = template.Title;
                task.Description = template.Description;
                details.Location = template.Location;
                details.IsPrivate = template.IsPrivate;
                details.IsAllDay = template.IsAllDay;
            }
            if (newPlacement is not null)
            {
                task.ProjectId = newPlacement.ProjectId;
                task.ObjectiveId = newPlacement.ObjectiveId;
            }
        }

        // 4. Surplus: cancelled when something hangs off it, otherwise soft-deleted (its key stays taken).
        foreach (var key in plan.Surplus.Where(k => !started.Contains(k)))
        {
            var old = byKey[key];
            if (await _meetings.HasDependentsAsync(tenantId, old.TaskId, ct))
                (await _meetings.GetDetailsTrackedAsync(tenantId, old.TaskId, ct))!.CancelledAt = now;
            else
                _meetings.RemoveMeetingTask((await _meetings.GetMeetingTaskTrackedAsync(tenantId, old.TaskId, ct))!);
        }

        // 5. Suppressions travel with their slot; a one-off source's moved slot is suppressed so it never comes back.
        var suppressionsByKey = suppressed.ToDictionary(s => s.OriginalStart);
        foreach (var (oldKey, newKey) in plan.SuppressionCopies)
            _meetings.AddSuppression(new MeetingOccurrenceSuppression
            {
                SeriesId = target.Id, OriginalStart = newKey, TenantId = tenantId, Reason = suppressionsByKey[oldKey].Reason,
                MovedToTaskId = suppressionsByKey[oldKey].MovedToTaskId, CreatedByEmployeeId = actorEmployeeId, CreatedAt = now,
            });
        foreach (var (oldKey, newKey) in keptStarted)
            _meetings.AddSuppression(new MeetingOccurrenceSuppression
            {
                SeriesId = target.Id, OriginalStart = newKey, TenantId = tenantId, Reason = MeetingSuppressionReasons.MovedSingle,
                MovedToTaskId = byKey[oldKey].TaskId, CreatedByEmployeeId = actorEmployeeId, CreatedAt = now,
            });
        if (source.RecurrenceRule is null)
            foreach (var (oldKey, _) in remaps)
                _meetings.AddSuppression(new MeetingOccurrenceSuppression
                {
                    SeriesId = source.Id, OriginalStart = oldKey, TenantId = tenantId, Reason = MeetingSuppressionReasons.MovedSingle,
                    MovedToTaskId = byKey[oldKey].TaskId, CreatedByEmployeeId = actorEmployeeId, CreatedAt = now,
                });

        // 6. Missing occurrences inside the 7-day window.
        var taken = plan.Remaps.Select(r => r.NewKey).Concat(plan.SuppressionCopies.Select(c => c.NewKey)).ToHashSet();   // kept-started slots included
        foreach (var key in newExpansion.Where(k => k >= now && k < now + MeetingSeriesFactory.Window && !taken.Contains(k)))
            await _materializer.MaterializeAsync(tenantId, target, key, actorUserId, ct);

        return Result<MeetingSeries>.Success(target);
    }
}
