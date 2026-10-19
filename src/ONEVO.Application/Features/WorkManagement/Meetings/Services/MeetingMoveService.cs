using System.Text.Json;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>The meeting.move approval payload: what to move and where.</summary>
public sealed record MeetingMovePayload(Guid SeriesId, DateTimeOffset OriginalStart, string Scope, Guid ProjectId, Guid ObjectiveId);

/// <summary>
/// Spec 5.5 / D9 / D20 moves, organizer only, forward-only:
/// - "following" (move_forward): a split at the first occurrence not started yet - computed when the move is applied -
///   into the destination module; historical occurrences keep their module;
/// - "this" (move_single, not started): a one-off series at the destination takes the occurrence with the same task id,
///   and a suppression keeps its old slot from ever being generated again.
/// The destination follows D4: direct for its owners, a meeting.move request for its other members, 403 otherwise.
/// Every moved task gets a meeting_move TaskEditLog row.
/// </summary>
public sealed class MeetingMoveService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IOptions<MeetingsOptions> _options;
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingOccurrenceWriter _writer;
    private readonly IMeetingOccurrenceMaterializer _materializer;
    private readonly IMeetingAccessResolver _access;
    private readonly IMeetingPlacementResolver _placements;
    private readonly IWorkApprovalEngine _approvals;
    private readonly IMeetingSeriesSplitter _splitter;
    private readonly IMeetingRecurrence _recurrence;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly IMeetingLinkRepository _links;

    public MeetingMoveService(IOptions<MeetingsOptions> options, IMeetingRepository meetings, IMeetingOccurrenceWriter writer,
        IMeetingOccurrenceMaterializer materializer, IMeetingAccessResolver access, IMeetingPlacementResolver placements,
        IWorkApprovalEngine approvals, IMeetingSeriesSplitter splitter, IMeetingRecurrence recurrence, IUnitOfWork unitOfWork,
        IDateTimeProvider clock, IMeetingLinkRepository links)
    {
        _links = links;
        _options = options;
        _meetings = meetings;
        _writer = writer;
        _materializer = materializer;
        _access = access;
        _placements = placements;
        _approvals = approvals;
        _splitter = splitter;
        _recurrence = recurrence;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<MeetingMoveResponse>> MoveAsync(Guid tenantId, Guid caller, Guid callerUserId, Guid seriesId,
        DateTimeOffset originalStart, MoveMeetingRequest move, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<MeetingMoveResponse>(_options.Value) is { } blocked)
            return blocked;
        if (move.Scope is not (MeetingScopes.This or MeetingScopes.Following))
            return Result<MeetingMoveResponse>.Failure("Move this occurrence or this and the following ones.", 400, MeetingErrorCodes.ScopeInvalid);
        if (await MeetingGuard.OrganizerOnlyAsync<MeetingMoveResponse>(_access, tenantId, caller, seriesId, originalStart, ct) is { } denied)
            return denied;
        var series = (await _meetings.GetSeriesAsync(tenantId, seriesId, ct))!;
        if (move.ProjectId == series.ProjectId && move.ObjectiveId == series.ObjectiveId)
            return Result<MeetingMoveResponse>.Failure("The meeting is already in this module.", 400, MeetingErrorCodes.SameModule);

        var destination = await _placements.ResolveAsync(tenantId, caller, move.ProjectId, move.ObjectiveId, ct);
        if (!destination.IsSuccess)
            return Result<MeetingMoveResponse>.Failure(destination.Error!, destination.StatusCode ?? 400, destination.ErrorCode);
        var payload = new MeetingMovePayload(seriesId, originalStart.ToUniversalTime(), move.Scope, move.ProjectId, move.ObjectiveId);

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                if (!destination.Value!.Direct)
                {
                    var decision = await _approvals.SubmitAsync(new WorkAction(tenantId, move.ProjectId, caller, WorkActionTypes.MeetingMove,
                        WorkTargetTypes.Meeting, seriesId, series.Title, move.ObjectiveId, PositionModuleId: null,
                        JsonSerializer.Serialize(payload, Json), TargetUpdatedAt: series.UpdatedAt), innerCt);
                    if (!decision.IsSuccess)
                        return Result<MeetingMoveResponse>.Failure(decision.Error!, decision.StatusCode ?? 400, decision.ErrorCode);
                    if (!decision.Value!.IsDirect)
                    {
                        await _unitOfWork.SaveChangesAsync(innerCt);
                        return Result<MeetingMoveResponse>.Success(new MeetingMoveResponse("requested", null, decision.Value.ApprovalRequestId));
                    }
                }
                var applied = await ApplyAsync(tenantId, caller, callerUserId, payload, innerCt);
                return applied.IsSuccess
                    ? Result<MeetingMoveResponse>.Success(new MeetingMoveResponse("moved", applied.Value, null))
                    : Result<MeetingMoveResponse>.Failure(applied.Error!, applied.StatusCode ?? 409, applied.ErrorCode);
            }, ct);
        }
        catch (MeetingBusyException)
        {
            return MeetingGuard.Busy<MeetingMoveResponse>();
        }
    }

    /// <summary>Applies a move inside the caller's transaction (direct, or an approved meeting.move); the id of the series
    /// that now holds the moved occurrences. 409 meeting_nothing_to_move / meeting_occurrence_started when, at apply time,
    /// nothing upcoming is left to move.</summary>
    public async Task<Result<Guid>> ApplyAsync(Guid tenantId, Guid actorEmployeeId, Guid actorUserId, MeetingMovePayload move, CancellationToken ct)
    {
        await _writer.LockSeriesAsync(tenantId, move.SeriesId, ct);
        var series = await _meetings.GetSeriesTrackedAsync(tenantId, move.SeriesId, ct);
        if (series is null)
            return MeetingReadService.NotFound<Guid>();
        if (series.CancelledAt is not null)
            return NothingToMove();
        var destination = new MeetingPlacement(move.ProjectId, move.ObjectiveId, IsOffice: false, Direct: true);
        var now = _clock.UtcNow;
        var key = move.OriginalStart.ToUniversalTime();
        var from = (series.ProjectId, series.ObjectiveId);

        Guid targetId;
        if (move.Scope == MeetingScopes.This)
        {
            var current = await _meetings.GetDetailsAsync(tenantId, series.Id, key, ct);
            if ((current?.StartAt ?? key) <= now)
                return Result<Guid>.Failure("This occurrence has already started.", 409, MeetingErrorCodes.OccurrenceStarted);
            var ensured = await _materializer.EnsureAsync(tenantId, series, key, actorUserId, ct);
            if (!ensured.IsSuccess)
                return ensured;
            var details = (await _meetings.GetDetailsAsync(tenantId, series.Id, key, ct))!;
            var task = (await _meetings.GetMeetingTaskTrackedAsync(tenantId, ensured.Value, ct))!;
            var target = new MeetingSeries
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = move.ProjectId, ObjectiveId = move.ObjectiveId,
                OrganizerEmployeeId = series.OrganizerEmployeeId, Title = task.Title, Description = task.Description,
                Location = details.Location, IsPrivate = details.IsPrivate, DtStart = details.StartAt,
                DurationMinutes = (int)(details.EndAt - details.StartAt).TotalMinutes, IsAllDay = details.IsAllDay,
                Timezone = series.Timezone, RecurrenceRule = null, CreationPath = series.CreationPath, SplitFromSeriesId = series.Id,
                SplitReason = MeetingSplitReasons.MoveSingle, SplitAt = key, CreatedAt = now, CreatedById = actorUserId,
            };
            _meetings.AddSeries(target);
            _meetings.AddParticipants((await _meetings.ListSeriesParticipantsAsync(tenantId, series.Id, ct)).Where(p => !p.IsDeleted)
                .Select(p => new CalendarEventParticipant
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, MeetingSeriesId = target.Id, EmployeeId = p.EmployeeId,
                    ResponseStatus = p.ResponseStatus, ResponseReason = p.ResponseReason, RespondedAt = p.RespondedAt,
                }));
            _meetings.AddGuests((await _meetings.ListSeriesGuestsAsync(tenantId, series.Id, ct)).Select(g => new CalendarEventGuest
            {
                Id = Guid.NewGuid(), TenantId = tenantId, MeetingSeriesId = target.Id, Email = g.Email, CreatedAt = now,
            }));
            _meetings.AddSuppression(new MeetingOccurrenceSuppression
            {
                SeriesId = series.Id, OriginalStart = key, TenantId = tenantId, Reason = MeetingSuppressionReasons.MovedSingle,
                MovedToTaskId = task.Id, CreatedByEmployeeId = actorEmployeeId, CreatedAt = now,
            });
            task.ProjectId = move.ProjectId;
            task.ObjectiveId = move.ObjectiveId;
            await _links.AddInheritedReferenceAsync(tenantId, series.Id, target.Id, now, ct);   // the moved occurrence keeps its link
            series.Revision++;
            series.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            await _writer.LockSeriesAsync(tenantId, target.Id, ct);
            await _writer.RemapIntoSeriesAsync(tenantId, target.Id, [(task.Id, details.StartAt, details.StartAt, details.EndAt)], ct);
            Log(tenantId, actorEmployeeId, actorUserId, task.Id, from, destination, now);
            targetId = target.Id;
        }
        else
        {
            var since = key > now ? key : now.AddTicks(1);
            DateTimeOffset? next = series.RecurrenceRule is null
                ? (series.DtStart >= since ? series.DtStart : null)
                : _recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, since, since.AddDays(400))
                    .Select(o => (DateTimeOffset?)o).FirstOrDefault();
            if (next is not { } point)
                return NothingToMove();
            var consumed = series.RecurrenceRule is null
                ? 0
                : _recurrence.Occurrences(series.RecurrenceRule, series.DtStart, series.Timezone, series.DtStart, point).Count;
            var template = new SeriesTemplate(series.Title, series.Description, series.Location, series.IsPrivate, point,
                series.DurationMinutes, series.IsAllDay, series.Timezone, MeetingRules.Remaining(series.RecurrenceRule, consumed));
            var split = await _splitter.SplitAsync(tenantId, series, point, MeetingSplitReasons.MoveForward, template, destination,
                actorEmployeeId, actorUserId, ct);
            if (!split.IsSuccess)
                return Result<Guid>.Failure(split.Error!, split.StatusCode ?? 409, split.ErrorCode);
            await _unitOfWork.SaveChangesAsync(ct);
            targetId = split.Value!.Id;
            // Moved tasks keep their old ids; occurrences the split created fresh at the destination were never moved.
            foreach (var moved in (await _meetings.ListOccurrencesFromAsync(tenantId, targetId, point, ct))
                         .Where(d => d.TaskId != MeetingOccurrenceIds.For(targetId, d.OriginalStart)))
                Log(tenantId, actorEmployeeId, actorUserId, moved.TaskId, from, destination, now);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result<Guid>.Success(targetId);
    }

    private void Log(Guid tenantId, Guid actorEmployeeId, Guid actorUserId, Guid taskId, (Guid ProjectId, Guid ObjectiveId) from,
        MeetingPlacement to, DateTimeOffset now)
        => _meetings.AddEditLog(new TaskEditLog
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, EmployeeId = actorEmployeeId, Source = TaskEditLogSources.MeetingMove,
            ChangedAt = now, CreatedById = actorUserId,
            OldValuesJson = JsonSerializer.Serialize(new { projectId = from.ProjectId, objectiveId = from.ObjectiveId }, Json),
            NewValuesJson = JsonSerializer.Serialize(new { projectId = to.ProjectId, objectiveId = to.ObjectiveId }, Json),
        });

    private static Result<Guid> NothingToMove()
        => Result<Guid>.Failure("No upcoming occurrence is left to move.", 409, MeetingErrorCodes.NothingToMove);
}
