using Microsoft.Extensions.Options;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>
/// Spec 5.3 cancel, organizer only, idempotent:
/// - "this": the one occurrence (materialized if needed);
/// - "following": every occurrence from this one on, and the rule ends just before it;
/// - "all": the series and every future occurrence.
/// A started or past occurrence cannot be cancelled. Plan C releases the link reference and sends the cancellations.
/// </summary>
public sealed class MeetingCancelService
{
    private readonly IOptions<MeetingsOptions> _options;
    private readonly IMeetingRepository _meetings;
    private readonly IMeetingOccurrenceWriter _writer;
    private readonly IMeetingOccurrenceMaterializer _materializer;
    private readonly IMeetingAccessResolver _access;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public MeetingCancelService(IOptions<MeetingsOptions> options, IMeetingRepository meetings, IMeetingOccurrenceWriter writer,
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

    public async Task<Result<bool>> CancelAsync(Guid tenantId, Guid callerEmployeeId, Guid callerUserId, Guid seriesId,
        DateTimeOffset originalStart, string scope, CancellationToken ct)
    {
        if (MeetingGuard.CheckWritable<bool>(_options.Value) is { } blocked)
            return blocked;
        if (scope is not (MeetingScopes.This or MeetingScopes.Following or MeetingScopes.All))
            return Result<bool>.Failure("Choose this, following or all occurrences.", 400, MeetingErrorCodes.ScopeInvalid);
        if (await MeetingGuard.OrganizerOnlyAsync<bool>(_access, tenantId, callerEmployeeId, seriesId, originalStart, ct) is { } denied)
            return denied;

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                await _writer.LockSeriesAsync(tenantId, seriesId, innerCt);
                var series = await _meetings.GetSeriesTrackedAsync(tenantId, seriesId, innerCt);
                if (series is null)
                    return MeetingReadService.NotFound<bool>();
                if (series.CancelledAt is not null)
                    return Result<bool>.Success(true);   // already cancelled: idempotent

                var now = _clock.UtcNow;
                var key = originalStart.ToUniversalTime();
                var existing = await _meetings.GetDetailsAsync(tenantId, seriesId, key, innerCt);
                if (scope != MeetingScopes.All && existing?.CancelledAt is not null && scope == MeetingScopes.This)
                    return Result<bool>.Success(true);
                if (scope != MeetingScopes.All && (existing?.StartAt ?? key) <= now)
                    return Result<bool>.Failure("This occurrence has already started.", 409, MeetingErrorCodes.OccurrenceStarted);

                if (scope == MeetingScopes.This)
                {
                    var ensured = await _materializer.EnsureAsync(tenantId, series, key, callerUserId, innerCt);
                    if (!ensured.IsSuccess)
                        return Result<bool>.Failure(ensured.Error!, ensured.StatusCode ?? 404, ensured.ErrorCode);
                    var details = (await _meetings.GetDetailsTrackedAsync(tenantId, ensured.Value, innerCt))!;
                    details.CancelledAt = now;
                    details.Revision++;
                }
                else
                {
                    var from = scope == MeetingScopes.Following ? key : now;
                    foreach (var occurrence in await _meetings.ListOccurrencesFromAsync(tenantId, seriesId, from, innerCt))
                    {
                        if (occurrence.CancelledAt is not null || (scope == MeetingScopes.All && occurrence.StartAt <= now))
                            continue;
                        var details = (await _meetings.GetDetailsTrackedAsync(tenantId, occurrence.TaskId, innerCt))!;
                        details.CancelledAt = now;
                        details.Revision++;
                    }
                    if (scope == MeetingScopes.All || series.RecurrenceRule is null || key <= series.DtStart)
                        series.CancelledAt = now;
                    else
                        series.RecurrenceRule = MeetingRules.WithUntil(series.RecurrenceRule, key.AddSeconds(-1));
                    series.Revision++;
                    series.UpdatedAt = now;
                }

                await _unitOfWork.SaveChangesAsync(innerCt);
                return Result<bool>.Success(true);
            }, ct);
        }
        catch (MeetingBusyException)
        {
            return MeetingGuard.Busy<bool>();
        }
    }
}
