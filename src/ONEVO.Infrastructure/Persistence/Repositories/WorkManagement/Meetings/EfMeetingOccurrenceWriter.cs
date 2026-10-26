using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;

/// <summary>
/// Spec 5.6 on PostgreSQL. The insert is one statement: a candidate row exists only when the occurrence key is free and
/// not suppressed; the task and its meeting_details row are inserted together, each ON CONFLICT DO NOTHING. A writer
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
        // Wait at most 5 s for the lock (spec 7 meeting_busy); LOCAL ends with this transaction.
        await _db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'", ct);
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({tenantId.ToString()}::text || ':' || {seriesId.ToString()}::text, 0))", ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new MeetingBusyException(seriesId, ex);
        }

        // The 5 s bound is for the series lock only. The rest of the transaction (e.g. the project row behind the
        // meeting number, which task creates take too) waits like any other write instead of failing with an
        // unmapped lock_not_available.
        await _db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = DEFAULT", ct);
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

        // Bare ON CONFLICT DO NOTHING on tasks: every unique index is an arbiter. With a single target (id), a racing
        // writer could hit ux_tasks_tenant_id_id (same id) first and fail with 23505 instead of doing nothing.
        var inserted = await _db.Database.SqlQuery<Guid>($"""
            WITH candidate AS (
                SELECT CASE WHEN EXISTS (SELECT 1 FROM tasks x WHERE x.id = {preferredId}::uuid)
                            THEN gen_random_uuid() ELSE {preferredId}::uuid END AS id
                WHERE NOT EXISTS (SELECT 1 FROM meeting_details md
                                  WHERE md.series_id = {d.SeriesId}::uuid AND md.original_start = {originalStart}::timestamptz)
                  AND NOT EXISTS (SELECT 1 FROM meeting_occurrence_suppressions s
                                  WHERE s.series_id = {d.SeriesId}::uuid AND s.original_start = {originalStart}::timestamptz)
            ), t AS (
                INSERT INTO tasks (id, tenant_id, task_kind, visibility_scope, project_id, objective_id, short_id, title,
                                   description, category_id, status_id, priority, completed_hours, progress_percent,
                                   created_at, created_by_id, is_deleted)
                SELECT id, {d.TenantId}::uuid, {WorkTaskKinds.Meeting}::text, {WorkTaskVisibilityScopes.Module}::text,
                       {d.ProjectId}::uuid, {d.ObjectiveId}::uuid, {d.ShortId}::text, {d.Title}::text,
                       {d.Description}::text, {d.CategoryId}::uuid, {d.StatusId}::uuid, {WorkTaskPriorities.Medium}::text,
                       0, 0, now(), {d.CreatedById}::uuid, false
                FROM candidate
                ON CONFLICT DO NOTHING
                RETURNING id
            ), details AS (
                INSERT INTO meeting_details (task_id, tenant_id, series_id, original_start, start_at, end_at, is_all_day,
                                             location, is_private, is_detached, revision, calendar_event_meeting_id,
                                             attendance_checkpoint, attendance_status, attendance_next_check_at)
                SELECT id, {d.TenantId}::uuid, {d.SeriesId}::uuid, {originalStart}::timestamptz, {startAt}::timestamptz,
                       {endAt}::timestamptz, {d.IsAllDay}::boolean, {d.Location}::text, {d.IsPrivate}::boolean, false, 0,
                       {d.CalendarEventMeetingId}::uuid, 0, {d.AttendanceStatus}::text,
                       {d.AttendanceNextCheckAt?.ToUniversalTime()}::timestamptz
                FROM t
                ON CONFLICT ON CONSTRAINT uq_meeting_details_series_original_start DO NOTHING
                RETURNING task_id
            )
            -- The details id when the pair went in; Guid.Empty when a task went in but its details row lost the race to a
            -- concurrent writer (only possible on the fresh-id path); no row when nothing was inserted.
            SELECT v AS "Value"
            FROM (SELECT COALESCE((SELECT task_id FROM details),
                                  CASE WHEN EXISTS (SELECT 1 FROM t) THEN '00000000-0000-0000-0000-000000000000'::uuid END) AS v) r
            WHERE v IS NOT NULL
            """).ToListAsync(ct); // not composed: a data-modifying WITH must stay top-level

        if (inserted.Count == 1)
        {
            // The orphan task would fail the deferred pairing trigger at COMMIT anyway; say so now, retryably.
            if (inserted[0] == Guid.Empty)
                throw new MeetingOccurrenceRaceLostException(d.SeriesId, originalStart);
            return inserted[0];
        }

        // Nothing inserted. Legitimate only when the slot is taken or suppressed (a racing writer's row is committed by
        // now - ON CONFLICT waited for it); anything else is a task-side conflict such as a reused ShortId.
        var slotTakenOrSuppressed = await _db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM meeting_details md
                           WHERE md.series_id = {d.SeriesId}::uuid AND md.original_start = {originalStart}::timestamptz)
                OR EXISTS (SELECT 1 FROM meeting_occurrence_suppressions s
                           WHERE s.series_id = {d.SeriesId}::uuid AND s.original_start = {originalStart}::timestamptz)
                AS "Value"
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
            $"SELECT EXISTS (SELECT 1 FROM meeting_details WHERE series_id = {targetSeriesId}::uuid) AS \"Value\"").ToListAsync(ct);
        if (targetHasRows.Single())
            throw new InvalidOperationException($"Target series {targetSeriesId} already has occurrences; remaps only fill an empty series.");

        var sql = new StringBuilder(
            "UPDATE meeting_details d SET series_id = @target, original_start = m.original_start, start_at = m.start_at, " +
            "end_at = m.end_at, revision = d.revision + 1 FROM (VALUES ");
        var parameters = new List<NpgsqlParameter>
        {
            new("target", NpgsqlDbType.Uuid) { Value = targetSeriesId },
            new("tenant", NpgsqlDbType.Uuid) { Value = tenantId },
        };
        for (var i = 0; i < mapping.Count; i++)
        {
            var (taskId, newOriginalStart, newStartAt, newEndAt) = mapping[i];
            var n = i.ToString(CultureInfo.InvariantCulture);
            sql.Append(i == 0 ? "" : ", ")
                .Append($"(@task{n}, @original{n}, @start{n}, @end{n})");
            parameters.Add(new NpgsqlParameter($"task{n}", NpgsqlDbType.Uuid) { Value = taskId });
            parameters.Add(new NpgsqlParameter($"original{n}", NpgsqlDbType.TimestampTz) { Value = newOriginalStart.ToUniversalTime() });
            parameters.Add(new NpgsqlParameter($"start{n}", NpgsqlDbType.TimestampTz) { Value = newStartAt.ToUniversalTime() });
            parameters.Add(new NpgsqlParameter($"end{n}", NpgsqlDbType.TimestampTz) { Value = newEndAt.ToUniversalTime() });
        }
        sql.Append(") AS m(task_id, original_start, start_at, end_at) WHERE d.task_id = m.task_id AND d.tenant_id = @tenant");

        var updated = await _db.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
        if (updated != mapping.Count)
            throw new InvalidOperationException($"Remap updated {updated} of {mapping.Count} occurrences; every task must be a meeting occurrence of this tenant.");
    }

    // The series lock is transaction-scoped and an orphaned task only fails at COMMIT: outside a transaction neither
    // guarantee holds, so every write is refused there.
    private void RequireTransaction(string operation)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException($"{operation} must run inside a database transaction.");
    }
}
