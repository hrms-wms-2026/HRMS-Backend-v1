using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;

namespace ONEVO.Infrastructure.Persistence.Interceptors;

/// <summary>
/// The rules PostgreSQL enforced with deferred constraint triggers: a meeting task and its
/// meeting_details row exist together, and a meeting task sits at its series' project and module.
/// SQL Server has no commit-time trigger, so touched rows are recorded on save and checked just
/// before commit. Each rule may be broken mid-transaction, as the pair is written in two steps.
/// Writes that bypass the change tracker must add to ApplicationDbContext.PendingMeetingTaskIds.
/// </summary>
public sealed class MeetingIntegrityInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private const string MeetingKind = "meeting";

    public static MeetingIntegrityInterceptor Instance { get; } = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Track(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Track(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public InterceptionResult TransactionCommitting(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
    {
        CheckAsync(transaction, eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        await CheckAsync(transaction, eventData.Context, cancellationToken);
        return result;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => Clear(eventData.Context);

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Clear(eventData.Context);
        return Task.CompletedTask;
    }

    private static void Clear(DbContext? context)
    {
        if (context is ApplicationDbContext db)
        {
            db.PendingMeetingTaskIds.Clear();
            db.PendingMeetingSeriesIds.Clear();
        }
    }

    private static void Track(DbContext? context)
    {
        if (context is not ApplicationDbContext db || !db.Database.IsSqlServer())
        {
            return;
        }

        foreach (var entry in db.ChangeTracker.Entries())
        {
            switch (entry.Entity)
            {
                case WorkTask task when IsRelevant(entry, task):
                    db.PendingMeetingTaskIds.Add(task.Id);
                    break;
                case MeetingDetails details when entry.State is EntityState.Added or EntityState.Deleted
                    || (entry.State == EntityState.Modified && entry.Property(nameof(MeetingDetails.SeriesId)).IsModified):
                    db.PendingMeetingTaskIds.Add(details.TaskId);
                    break;
                case MeetingSeries series when entry.State == EntityState.Modified
                    && (entry.Property(nameof(MeetingSeries.ProjectId)).IsModified
                        || entry.Property(nameof(MeetingSeries.ObjectiveId)).IsModified):
                    db.PendingMeetingSeriesIds.Add(series.Id);
                    break;
            }
        }

        // A single-statement save gets no transaction, so the commit check would never run.
        if ((db.PendingMeetingTaskIds.Count > 0 || db.PendingMeetingSeriesIds.Count > 0)
            && db.Database.CurrentTransaction is null)
        {
            db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
        }
    }

    private static bool IsRelevant(EntityEntry entry, WorkTask task)
    {
        var kind = entry.Property(nameof(WorkTask.TaskKind));
        return entry.State switch
        {
            EntityState.Added or EntityState.Deleted => task.TaskKind == MeetingKind,
            EntityState.Modified => kind.IsModified
                || (task.TaskKind == MeetingKind
                    && (entry.Property(nameof(WorkTask.ProjectId)).IsModified
                        || entry.Property(nameof(WorkTask.ObjectiveId)).IsModified)),
            _ => false,
        };
    }

    private static async Task CheckAsync(DbTransaction transaction, DbContext? context, CancellationToken ct)
    {
        if (context is not ApplicationDbContext db
            || (db.PendingMeetingTaskIds.Count == 0 && db.PendingMeetingSeriesIds.Count == 0))
        {
            return;
        }

        var taskIds = JsonSerializer.Serialize(db.PendingMeetingTaskIds);
        var seriesIds = JsonSerializer.Serialize(db.PendingMeetingSeriesIds);
        Clear(db);

        var connection = transaction.Connection ?? db.Database.GetDbConnection();
        var (tenantId, mode) = await ReadContextAsync(connection, transaction, ct);

        // Admin context: a check that could not see a row would pass instead of failing.
        await SetContextAsync(connection, transaction, string.Empty, "admin", ct);
        string? violation;
        try
        {
            violation = await FindViolationAsync(connection, transaction, taskIds, seriesIds, ct);
        }
        finally
        {
            await SetContextAsync(connection, transaction, tenantId, mode, ct);
        }

        if (violation is not null)
        {
            throw new InvalidOperationException(violation);
        }
    }

    private static async Task<string?> FindViolationAsync(
        DbConnection connection, DbTransaction transaction, string taskIds, string seriesIds, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @tasks TABLE (id uniqueidentifier PRIMARY KEY);
            INSERT INTO @tasks SELECT DISTINCT CAST([value] AS uniqueidentifier) FROM OPENJSON(@task_ids);
            INSERT INTO @tasks
            SELECT DISTINCT d.task_id FROM dbo.meeting_details d
            JOIN OPENJSON(@series_ids) s ON CAST(s.[value] AS uniqueidentifier) = d.series_id
            WHERE NOT EXISTS (SELECT 1 FROM @tasks x WHERE x.id = d.task_id);

            SELECT TOP (1) message FROM (
                SELECT N'trg_meeting_task_has_details: meeting task ' + CAST(t.id AS nvarchar(36))
                       + N' has no meeting_details row.' AS message
                FROM @tasks p
                JOIN dbo.tasks t ON t.id = p.id
                WHERE t.task_kind = N'meeting'
                  AND NOT EXISTS (SELECT 1 FROM dbo.meeting_details d WHERE d.task_id = t.id)
                UNION ALL
                SELECT N'trg_meeting_task_has_details: meeting_details ' + CAST(d.task_id AS nvarchar(36))
                       + N' belongs to a task that is not a meeting.'
                FROM @tasks p
                JOIN dbo.meeting_details d ON d.task_id = p.id
                LEFT JOIN dbo.tasks t ON t.id = d.task_id
                WHERE t.id IS NULL OR t.task_kind <> N'meeting'
                UNION ALL
                SELECT N'trg_meeting_placement_matches_series: meeting task ' + CAST(t.id AS nvarchar(36))
                       + N' is not at its series'' project and module.'
                FROM @tasks p
                JOIN dbo.meeting_details d ON d.task_id = p.id
                JOIN dbo.tasks t ON t.id = d.task_id
                JOIN dbo.meeting_series s ON s.id = d.series_id
                WHERE t.project_id <> s.project_id
                   OR (t.objective_id <> s.objective_id)
                   OR (t.objective_id IS NULL AND s.objective_id IS NOT NULL)
                   OR (t.objective_id IS NOT NULL AND s.objective_id IS NULL)
            ) violations;
            """;
        AddParameter(command, "task_ids", taskIds);
        AddParameter(command, "series_ids", seriesIds);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    private static async Task<(string TenantId, string Mode)> ReadContextAsync(
        DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CAST(SESSION_CONTEXT(N'app.current_tenant_id') AS nvarchar(36)),
                   CAST(SESSION_CONTEXT(N'app.tenant_context_mode') AS nvarchar(16))
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1));
    }

    private static async Task SetContextAsync(
        DbConnection connection, DbTransaction transaction, string tenantId, string mode, CancellationToken ct)
    {
        await using var command = TenantRlsInterceptor.SetTenantCommand(connection, tenantId, mode);
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
