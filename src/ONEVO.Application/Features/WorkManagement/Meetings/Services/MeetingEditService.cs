using System.Text.Json;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// Spec 5.3 edits, organizer only:
/// - "this": materialize if needed, edit the task and its details, detach it, keep its key;
/// - "all": the series' non-key fields, applied to the series and every future, not-started, non-detached occurrence;
/// - a started or past occurrence takes only notes (description, D19), audited, with no email.
/// - "following", and a time or rule change of "all": a forward split (spec 5.5) at this occurrence, or at the first
///   occurrence not started yet.
/// </summary>
public sealed class MeetingEditService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IOptions<MeetingsOptions> _options;
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingOccurrenceWriter _writer;
    private readonly IMeetingOccurrenceMaterializer _materializer;
    private readonly IMeetingAccessResolver _access;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly IMeetingSeriesSplitter _splitter;
    private readonly IMeetingNotifier _notifier;
    private readonly IMeetingRecurrence _recurrence;

    public MeetingEditService(IOptions<MeetingsOptions> options, IMeetingRepository meetings, IMeetingOccurrenceWriter writer,
        IMeetingOccurrenceMaterializer materializer, IMeetingAccessResolver access, IUnitOfWork unitOfWork, IDateTimeProvider clock,
        IMeetingSeriesSplitter splitter, IMeetingRecurrence recurrence,
        IMeetingNotifier notifier)
    {
        _splitter = splitter;
        _notifier = notifier;
        _recurrence = recurrence;
        _options = options;
        _meetings = meetings;
        _writer = writer;
        _materializer = materializer;
        _access = access;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<MeetingEditResponse>> EditAsync(Guid tenantId, Guid callerEmployeeId, Guid callerUserId, Guid seriesId,
        DateTimeOffset originalStart, EditMeetingRequest edit, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<MeetingEditResponse>(_options.Value) is { } blocked)
            return blocked;
        if (edit.Scope is not (MeetingScopes.This or MeetingScopes.All or MeetingScopes.Following))
            return Result<MeetingEditResponse>.Failure("Choose this, following or all occurrences.", 400, MeetingErrorCodes.ScopeInvalid);
        if (MeetingCreateValidator.CheckLengths(edit.Location, edit.RecurrenceRule) is { } tooLong)
            return Result<MeetingEditResponse>.Failure(tooLong.Error!, 400, tooLong.ErrorCode);
        if (await MeetingGuard.OrganizerOnlyAsync<MeetingEditResponse>(_access, tenantId, callerEmployeeId, seriesId, originalStart, ct) is { } denied)
            return denied;

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                await _writer.LockSeriesAsync(tenantId, seriesId, innerCt);
                var series = await _meetings.GetSeriesTrackedAsync(tenantId, seriesId, innerCt);
                if (series is null)
                    return MeetingReadService.NotFound<MeetingEditResponse>();
                var result = edit.Scope == MeetingScopes.This
                    ? await EditOccurrenceAsync(tenantId, callerEmployeeId, callerUserId, series, originalStart, edit, innerCt)
                    : await EditSeriesAsync(tenantId, callerEmployeeId, callerUserId, series, originalStart, edit, innerCt);
                if (result.IsSuccess)
                    await _unitOfWork.SaveChangesAsync(innerCt);
                return result;
            }, ct);
        }
        catch (MeetingBusyException)
        {
            return MeetingGuard.Busy<MeetingEditResponse>();
        }
    }

    private async Task<Result<MeetingEditResponse>> EditOccurrenceAsync(Guid tenantId, Guid callerEmployeeId, Guid callerUserId,
        MeetingSeries series, DateTimeOffset originalStart, EditMeetingRequest edit, CancellationToken ct)
    {
        if (edit.Timezone is not null || edit.RecurrenceRule is not null)
            return Result<MeetingEditResponse>.Failure("Timezone and repeat rule belong to the whole series.", 400, MeetingErrorCodes.ScopeInvalid);
        var ensured = await _materializer.EnsureAsync(tenantId, series, originalStart, callerUserId, ct);
        if (!ensured.IsSuccess)
            return Result<MeetingEditResponse>.Failure(ensured.Error!, ensured.StatusCode ?? 404, ensured.ErrorCode);
        var details = (await _meetings.GetDetailsTrackedAsync(tenantId, ensured.Value, ct))!;
        var task = (await _meetings.GetMeetingTaskTrackedAsync(tenantId, ensured.Value, ct))!;
        if (edit.Revision != details.Revision)
            return Changed();

        var now = _clock.UtcNow;
        var cancelled = details.CancelledAt is not null || MeetingStatusCalculator.CancelledWithSeries(details, series);
        if (now >= details.StartAt || cancelled || series.CancelledAt is not null)
        {
            var onlyNotes = edit is { Title: null, Location: null, IsPrivate: null, Start: null, DurationMinutes: null, IsAllDay: null }
                && edit.Description is not null && !cancelled && now >= details.StartAt;
            if (!onlyNotes)
                return Result<MeetingEditResponse>.Failure(
                    "This occurrence has started; only its notes can still change.", 409, MeetingErrorCodes.OccurrenceStarted);
            _meetings.AddEditLog(new TaskEditLog
            {
                Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id, EmployeeId = callerEmployeeId,
                Source = TaskEditLogSources.MeetingNotes, ChangedAt = now, CreatedById = callerUserId,
                OldValuesJson = JsonSerializer.Serialize(new { description = task.Description }, Json),
                NewValuesJson = JsonSerializer.Serialize(new { description = edit.Description.Trim() }, Json),
            });
            task.Description = edit.Description.Trim();
            details.Revision++;
            return Result<MeetingEditResponse>.Success(new MeetingEditResponse(series.Id, task.Id, Split: false));
        }

        if (edit.Title is not null)
        {
            if (string.IsNullOrWhiteSpace(edit.Title) || edit.Title.Trim().Length > MeetingCreateValidator.MaxTitleLength)
                return Result<MeetingEditResponse>.Failure("A meeting needs a title.", 400, MeetingErrorCodes.TitleInvalid);
            task.Title = edit.Title.Trim();
        }
        if (edit.Description is not null)
            task.Description = edit.Description.Trim();
        if (edit.Location is not null)
            details.Location = edit.Location.Trim();
        if (edit.IsPrivate is bool isPrivate)
            details.IsPrivate = isPrivate;
        if (edit.IsAllDay is bool isAllDay)
            details.IsAllDay = isAllDay;
        if (edit.Start is not null || edit.DurationMinutes is not null)
        {
            var start = (edit.Start ?? details.StartAt).ToUniversalTime();
            var minutes = edit.DurationMinutes ?? (int)(details.EndAt - details.StartAt).TotalMinutes;
            if (minutes <= 0)
                return Result<MeetingEditResponse>.Failure("A meeting must end after it starts.", 400, MeetingErrorCodes.InvalidTime);
            if (start <= now)
                return Result<MeetingEditResponse>.Failure("A meeting must start in the future.", 400, MeetingErrorCodes.StartInPast);
            var newEnd = start.AddMinutes(minutes);
            // A pending attendance check is counted from the end (spec 5.8): it moves with it, or it would run
            // before the rescheduled meeting has ended.
            if (details.AttendanceNextCheckAt is { } nextCheck)
                details.AttendanceNextCheckAt = nextCheck + (newEnd - details.EndAt);
            details.StartAt = start;
            details.EndAt = newEnd;
        }
        details.IsDetached = true;
        details.Revision++;
        await _notifier.OccurrenceUpdatedAsync(series, details, task.Title, ct);
        return Result<MeetingEditResponse>.Success(new MeetingEditResponse(series.Id, task.Id, Split: false));
    }

    private async Task<Result<MeetingEditResponse>> EditSeriesAsync(Guid tenantId, Guid callerEmployeeId, Guid callerUserId,
        MeetingSeries series, DateTimeOffset originalStart, EditMeetingRequest edit, CancellationToken ct)
    {
        if (edit.Revision != series.Revision)
            return Changed();
        if (series.CancelledAt is not null)
            return Result<MeetingEditResponse>.Failure("This meeting was cancelled.", 409, MeetingErrorCodes.OccurrenceStarted);
        if (edit.Title is not null && (string.IsNullOrWhiteSpace(edit.Title) || edit.Title.Trim().Length > MeetingCreateValidator.MaxTitleLength))
            return Result<MeetingEditResponse>.Failure("A meeting needs a title.", 400, MeetingErrorCodes.TitleInvalid);
        var changesKey = edit.Start is not null || edit.DurationMinutes is not null || edit.IsAllDay is not null
            || edit.Timezone is not null || edit.RecurrenceRule is not null;
        if (edit.Scope == MeetingScopes.Following || changesKey)
            return await SplitAsync(tenantId, callerEmployeeId, callerUserId, series, originalStart, edit, ct);

        if (edit.Title is not null)
            series.Title = edit.Title.Trim();
        if (edit.Description is not null)
            series.Description = edit.Description.Trim();
        if (edit.Location is not null)
            series.Location = edit.Location.Trim();
        if (edit.IsPrivate is bool isPrivate)
            series.IsPrivate = isPrivate;
        series.Revision++;
        series.UpdatedAt = _clock.UtcNow;

        foreach (var (details, task) in await _meetings.ListTemplateOccurrencesTrackedAsync(tenantId, series.Id, _clock.UtcNow, ct))
        {
            task.Title = series.Title;
            task.Description = series.Description;
            details.Location = series.Location;
            details.IsPrivate = series.IsPrivate;
            details.Revision++;
        }

        await _notifier.SeriesUpdatedAsync(series, ct);
        var occurrence = await _meetings.GetDetailsAsync(tenantId, series.Id, originalStart, ct);
        return Result<MeetingEditResponse>.Success(new MeetingEditResponse(series.Id,
            occurrence?.TaskId ?? MeetingOccurrenceIds.For(series.Id, originalStart), Split: false));
    }

    /// <summary>"following": split at this occurrence; "all" with a time/rule change: at the first occurrence not started.
    /// The new start keeps the edited occurrence's shift (edit.Start minus its original start) for every later one.</summary>
    private async Task<Result<MeetingEditResponse>> SplitAsync(Guid tenantId, Guid callerEmployeeId, Guid callerUserId,
        MeetingSeries series, DateTimeOffset originalStart, EditMeetingRequest edit, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var key = originalStart.ToUniversalTime();
        DateTimeOffset point;
        if (edit.Scope == MeetingScopes.Following)
        {
            var details = await _meetings.GetDetailsAsync(tenantId, series.Id, key, ct);
            if ((details?.StartAt ?? key) <= now)
                return Result<MeetingEditResponse>.Failure(
                    "This occurrence has started; change the ones after it.", 409, MeetingErrorCodes.OccurrenceStarted);
            point = key;
        }
        else
        {
            DateTimeOffset? next = series.RecurrenceRule is null
                ? (series.DtStart > now ? series.DtStart : null)
                : _recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, now, now.AddDays(400))
                    .Select(o => (DateTimeOffset?)o).FirstOrDefault();
            if (next is not { } first)
                return Result<MeetingEditResponse>.Failure("No upcoming occurrence is left to change.", 409, MeetingErrorCodes.RecurrenceEmpty);
            point = first;
        }

        var shift = edit.Start is { } start ? start.ToUniversalTime() - key : TimeSpan.Zero;
        var consumed = series.RecurrenceRule is null
            ? 0
            : _recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, series.DtStart, point).Count;
        var rule = edit.RecurrenceRule ?? MeetingRules.Remaining(series.RecurrenceRule, consumed);
        if (series.RecurrenceRule is not null && edit.RecurrenceRule is null && rule is null)
            return Result<MeetingEditResponse>.Failure("No upcoming occurrence is left to change.", 409, MeetingErrorCodes.RecurrenceEmpty);
        var template = new SeriesTemplate(edit.Title?.Trim() ?? series.Title, edit.Description?.Trim() ?? series.Description,
            edit.Location?.Trim() ?? series.Location, edit.IsPrivate ?? series.IsPrivate, point + shift,
            edit.DurationMinutes ?? series.DurationMinutes, edit.IsAllDay ?? series.IsAllDay, edit.Timezone ?? series.Timezone, rule);
        if (template.DurationMinutes <= 0)
            return Result<MeetingEditResponse>.Failure("A meeting must end after it starts.", 400, MeetingErrorCodes.InvalidTime);
        if (template.DtStart <= now)
            return Result<MeetingEditResponse>.Failure("A meeting must start in the future.", 400, MeetingErrorCodes.StartInPast);
        var valid = _recurrence.Validate(template.RecurrenceRule, template.DtStart, template.Timezone);
        if (!valid.IsSuccess)
            return Result<MeetingEditResponse>.Failure(valid.Error!, valid.StatusCode ?? 400, valid.ErrorCode);

        var split = await _splitter.SplitAsync(tenantId, series, point, MeetingSplitReasons.EditFollowing, template, null,
            callerEmployeeId, callerUserId, ct);
        if (!split.IsSuccess)
            return Result<MeetingEditResponse>.Failure(split.Error!, split.StatusCode ?? 409, split.ErrorCode);
        var target = split.Value!;
        await _unitOfWork.SaveChangesAsync(ct);
        // The old series now ends before the point (its UNTIL); the rest arrives as a new series with its own UID.
        await _notifier.SeriesUpdatedAsync(series, ct);
        await _notifier.SeriesUpdatedAsync(target, ct);
        var firstOccurrence = await _meetings.GetDetailsAsync(tenantId, target.Id, template.DtStart, ct);
        return Result<MeetingEditResponse>.Success(new MeetingEditResponse(target.Id,
            firstOccurrence?.TaskId ?? MeetingOccurrenceIds.For(target.Id, template.DtStart), Split: true));
    }

    private static Result<MeetingEditResponse> Changed()
        => Result<MeetingEditResponse>.Failure("Someone else changed this meeting. Reload and try again.", 409, MeetingErrorCodes.Changed);
}
