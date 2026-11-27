using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Data;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Persistence.SqlServer;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

/// <summary>
/// Spec 5.6. The insert is one batch: it runs only when the occurrence key is free and not suppressed; the task and its
/// meeting_details row are inserted together, each treating a unique violation (2601, 2627) as nothing inserted. A writer
/// that loses the details race leaves a meeting task without details, which the deferred pairing trigger rejects at
/// commit - the generator retries, and the retry finds the key taken. Raw SQL on tasks: allow-listed in
/// MeetingQueryFilterArchitectureTests.
/// </summary>
public sealed class EfMeetingOccurrenceWriter : IMeetingOccurrenceWriter
{
    private readonly ApplicationDbContext _db;

    public EfMeetingOccurrenceWriter(ApplicationDbContext db) => _db = db;

    public async Task LockSeriesAsync(Guid tenantId, Guid seriesId, CancellationToken ct)
    {
        RequireTransaction(nameof(LockSeriesAsync));
        // Wait at most 5 s for the lock (spec 7 meeting_busy); it ends with this transaction.
        if (!await SqlAppLock.TryAcquireAsync(_db, $"meeting-series:{tenantId}:{seriesId}", 5000, ct))
            throw new MeetingBusyException(seriesId, new TimeoutException("The series lock was not free within 5 s."));
    }

    public async Task<Guid?> TryMaterializeAsync(MeetingOccurrenceDraft d, CancellationToken ct)
    {
        RequireTransaction(nameof(TryMaterializeAsync));
        var originalStart = d.OriginalStart.ToUniversalTime();
        var startAt = d.StartAt.ToUniversalTime();
        var endAt = d.EndAt.ToUniversalTime();
        // The deterministic id (spec 5.5) unless a task already holds it - an occurrence remapped into another series
        // keeps its id, so re-materializing its old slot needs a fresh one.
        var preferredId = MeetingOccurrenceIds.For(d.SeriesId, originalStart);

        // A unique violation means a racing writer won, which counts as nothing inserted: on tasks any unique index may
        // be the one hit. A violation does not doom the transaction, so the batch carries on.
        var inserted = await _db.Database.SqlQuery<Guid>($"""
            DECLARE @id uniqueidentifier, @task_in bit = 0, @details_in bit = 0;
            IF NOT EXISTS (SELECT 1 FROM meeting_details md
                           WHERE md.series_id = {d.SeriesId} AND md.original_start = {originalStart})
               AND NOT EXISTS (SELECT 1 FROM meeting_occurrence_suppressions s
                               WHERE s.series_id = {d.SeriesId} AND s.original_start = {originalStart})
            BEGIN
                SET @id = CASE WHEN EXISTS (SELECT 1 FROM tasks x WHERE x.id = {preferredId})
                               THEN NEWID() ELSE {preferredId} END;
                BEGIN TRY
                    INSERT INTO tasks (id, tenant_id, task_kind, visibility_scope, project_id, objective_id, short_id, title,
                                       description, category_id, status_id, priority, completed_hours, progress_percent,
                                       created_at, created_by_id, is_deleted)
                    VALUES (@id, {d.TenantId}, {WorkTaskKinds.Meeting}, {WorkTaskVisibilityScopes.Module},
                            {d.ProjectId}, {d.ObjectiveId}, {d.ShortId}, {d.Title},
                            {d.Description}, {d.CategoryId}, {d.StatusId}, {WorkTaskPriorities.Medium},
                            0, 0, SYSDATETIMEOFFSET(), {(d.CreatedById == Guid.Empty ? (Guid?)null : d.CreatedById)}, 0);
                    SET @task_in = 1;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
                END CATCH;
                IF @task_in = 1
                BEGIN TRY
                    INSERT INTO meeting_details (task_id, tenant_id, series_id, original_start, start_at, end_at, is_all_day,
                                                 location, is_private, is_detached, revision, calendar_event_meeting_id,
                                                 attendance_checkpoint, attendance_status, attendance_next_check_at)
                    VALUES (@id, {d.TenantId}, {d.SeriesId}, {originalStart}, {startAt},
                            {endAt}, {d.IsAllDay}, {d.Location}, {d.IsPrivate}, 0, 0,
                            {d.CalendarEventMeetingId}, 0, {d.AttendanceStatus},
                            {d.AttendanceNextCheckAt?.ToUniversalTime()});
                    SET @details_in = 1;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
                END CATCH;
            END;
            -- The task id when the pair went in; Guid.Empty when a task went in but its details row lost the race to a
            -- concurrent writer; no row when nothing was inserted.
            SELECT CASE WHEN @details_in = 1 THEN @id ELSE '00000000-0000-0000-0000-000000000000' END AS [Value]
            WHERE @task_in = 1;
            """).ToListAsync(ct); // not composed: a batch must stay top-level

        if (inserted.Count == 1)
        {
            // A task without its details row must not commit; say so now, retryably.
            if (inserted[0] == Guid.Empty)
                throw new MeetingOccurrenceRaceLostException(d.SeriesId, originalStart);
            return inserted[0];
        }

        // Nothing inserted. Legitimate only when the slot is taken or suppressed (a racing writer's row is committed by
        // now - the unique violation waited for it); anything else is a task-side conflict such as a reused ShortId.
        var slotTakenOrSuppressed = await _db.Database.SqlQuery<bool>($"""
            SELECT CASE WHEN EXISTS (SELECT 1 FROM meeting_details md
                                     WHERE md.series_id = {d.SeriesId} AND md.original_start = {originalStart})
                          OR EXISTS (SELECT 1 FROM meeting_occurrence_suppressions s
                                     WHERE s.series_id = {d.SeriesId} AND s.original_start = {originalStart})
                        THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS [Value]
            """).ToListAsync(ct);
        if (!slotTakenOrSuppressed.Single())
            throw new InvalidOperationException(
                $"Occurrence {d.SeriesId}@{originalStart:O} was not materialized: its task conflicted (ShortId '{d.ShortId}' already used?).");
        return null;
    }

    public async Task RemapIntoSeriesAsync(Guid tenantId, Guid targetSeriesId,
        IReadOnlyList<(Guid TaskId, DateTimeOffset NewOriginalStart, DateTimeOffset NewStartAt, DateTimeOffset NewEndAt)> mapping,
        CancellationToken ct)
    {
        RequireTransaction(nameof(RemapIntoSeriesAsync));
        if (mapping.Count == 0)
            return;
        // Strictly increasing, distinct keys in an empty target: no row can ever collide with another mid-statement,
        // which is what lets the occurrence unique constraint be NOT DEFERRABLE (spec 5.6).
        for (var i = 1; i < mapping.Count; i++)
            if (mapping[i].NewOriginalStart.ToUniversalTime() <= mapping[i - 1].NewOriginalStart.ToUniversalTime())
                throw new InvalidOperationException("Remapped occurrence starts must be strictly increasing and distinct.");
        if (mapping.Select(m => m.TaskId).Distinct().Count() != mapping.Count)
            throw new InvalidOperationException("Each occurrence can be remapped only once.");

        // Splits and moves write the target under its series lock, so the emptiness check below holds until commit.
        await LockSeriesAsync(tenantId, targetSeriesId, ct);
        var targetHasRows = await _db.Database.SqlQuery<bool>(
            $"SELECT CASE WHEN EXISTS (SELECT 1 FROM meeting_details WHERE series_id = {targetSeriesId}) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS [Value]").ToListAsync(ct);
        if (targetHasRows.Single())
            throw new InvalidOperationException($"Target series {targetSeriesId} already has occurrences; remaps only fill an empty series.");

        var sql = new StringBuilder(
            "UPDATE d SET series_id = @target, original_start = m.original_start, start_at = m.start_at, " +
            "end_at = m.end_at, revision = d.revision + 1, " +
            // A pending attendance check is counted from the end (spec 5.8), so it moves by the same shift.
            "attendance_next_check_at = DATEADD(second, DATEDIFF(second, d.end_at, m.end_at), d.attendance_next_check_at) " +
            "FROM meeting_details d JOIN (VALUES ");
        var parameters = new List<SqlParameter>
        {
            new("target", SqlDbType.UniqueIdentifier) { Value = targetSeriesId },
            new("tenant", SqlDbType.UniqueIdentifier) { Value = tenantId },
        };
        for (var i = 0; i < mapping.Count; i++)
        {
            var (taskId, newOriginalStart, newStartAt, newEndAt) = mapping[i];
            var n = i.ToString(CultureInfo.InvariantCulture);
            sql.Append(i == 0 ? "" : ", ")
                .Append($"(@task{n}, @original{n}, @start{n}, @end{n})");
            parameters.Add(new SqlParameter($"task{n}", SqlDbType.UniqueIdentifier) { Value = taskId });
            parameters.Add(new SqlParameter($"original{n}", SqlDbType.DateTimeOffset) { Value = newOriginalStart.ToUniversalTime() });
            parameters.Add(new SqlParameter($"start{n}", SqlDbType.DateTimeOffset) { Value = newStartAt.ToUniversalTime() });
            parameters.Add(new SqlParameter($"end{n}", SqlDbType.DateTimeOffset) { Value = newEndAt.ToUniversalTime() });
        }
        sql.Append(") AS m(task_id, original_start, start_at, end_at) ON d.task_id = m.task_id WHERE d.tenant_id = @tenant");

        // Raw SQL, so the commit check is told about these rows directly.
        _db.PendingMeetingTaskIds.UnionWith(mapping.Select(m => m.TaskId));
        var updated = await _db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
        if (updated != mapping.Count)
            throw new InvalidOperationException($"Remap updated {updated} of {mapping.Count} occurrences; every task must be a meeting occurrence of this tenant.");
    }

    // The series lock is transaction-scoped and an orphaned task only fails at commit: outside a transaction neither
    // guarantee holds, so every write is refused there.
    private void RequireTransaction(string operation)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException($"{operation} must run inside a database transaction.");
    }
}
