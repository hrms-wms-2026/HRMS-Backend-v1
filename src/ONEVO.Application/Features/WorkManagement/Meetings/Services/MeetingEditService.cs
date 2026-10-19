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
/// Time/rule changes of "all" and "following" are forward splits (Plan B Task 10).
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

    public MeetingEditService(IOptions<MeetingsOptions> options, IMeetingRepository meetings, IMeetingOccurrenceWriter writer,
        IMeetingOccurrenceMaterializer materializer, IMeetingAccessResolver access, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
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
        if (await OrganizerCheckAsync<MeetingEditResponse>(tenantId, callerEmployeeId, seriesId, originalStart, ct) is { } denied)
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
                    : await EditSeriesAsync(tenantId, callerUserId, series, originalStart, edit, innerCt);
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

    /// <summary>404 for anyone who cannot see the occurrence; 403 for an attendee; null for the organizer.</summary>
    internal async Task<Result<T>?> OrganizerCheckAsync<T>(Guid tenantId, Guid callerEmployeeId, Guid seriesId, DateTimeOffset originalStart, CancellationToken ct)
    {
        var access = await _access.ResolveForOccurrenceAsync(tenantId, callerEmployeeId, seriesId, originalStart, ct);
        if (access is null)
            return MeetingReadService.NotFound<T>();
        return access.Role == MeetingRoles.Organizer
            ? null
            : Result<T>.Failure("Only the organizer can change this meeting.", 403, MeetingErrorCodes.OrganizerRequired);
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
        if (now >= details.StartAt || details.CancelledAt is not null || series.CancelledAt is not null)
        {
            var onlyNotes = edit is { Title: null, Location: null, IsPrivate: null, Start: null, DurationMinutes: null, IsAllDay: null }
                && edit.Description is not null && details.CancelledAt is null && series.CancelledAt is null && now >= details.StartAt;
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
            details.StartAt = start;
            details.EndAt = start.AddMinutes(minutes);
        }
        details.IsDetached = true;
        details.Revision++;
        return Result<MeetingEditResponse>.Success(new MeetingEditResponse(series.Id, task.Id, Split: false));
    }

    private async Task<Result<MeetingEditResponse>> EditSeriesAsync(Guid tenantId, Guid callerUserId, MeetingSeries series,
        DateTimeOffset originalStart, EditMeetingRequest edit, CancellationToken ct)
    {
        if (edit.Revision != series.Revision)
            return Changed();
        var changesKey = edit.Start is not null || edit.DurationMinutes is not null || edit.IsAllDay is not null
            || edit.Timezone is not null || edit.RecurrenceRule is not null;
        if (edit.Scope == MeetingScopes.Following || changesKey)
            return Result<MeetingEditResponse>.Failure(
                "Changing the time or repeat rule of upcoming occurrences is not available yet.", 400, MeetingErrorCodes.ScopeInvalid);
        if (series.CancelledAt is not null)
            return Result<MeetingEditResponse>.Failure("This meeting was cancelled.", 409, MeetingErrorCodes.OccurrenceStarted);
        if (edit.Title is not null && (string.IsNullOrWhiteSpace(edit.Title) || edit.Title.Trim().Length > MeetingCreateValidator.MaxTitleLength))
            return Result<MeetingEditResponse>.Failure("A meeting needs a title.", 400, MeetingErrorCodes.TitleInvalid);

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

        var occurrence = await _meetings.GetDetailsAsync(tenantId, series.Id, originalStart, ct);
        return Result<MeetingEditResponse>.Success(new MeetingEditResponse(series.Id,
            occurrence?.TaskId ?? MeetingOccurrenceIds.For(series.Id, originalStart), Split: false));
    }

    private static Result<MeetingEditResponse> Changed()
        => Result<MeetingEditResponse>.Failure("Someone else changed this meeting. Reload and try again.", 409, MeetingErrorCodes.Changed);
}
