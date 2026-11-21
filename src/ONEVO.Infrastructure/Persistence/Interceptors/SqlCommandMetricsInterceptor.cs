using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ONEVO.Infrastructure.Persistence.Diagnostics;

namespace ONEVO.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Adds every EF Core command (reader, scalar, non-query; failed ones too) and its duration to the
/// scope's <see cref="RequestSqlMetrics"/>, so the request log line can show the SQL count and time
/// (Improvements task 06). Commands sent on a raw DbConnection outside EF are not counted.
/// </summary>
public sealed class SqlCommandMetricsInterceptor : DbCommandInterceptor
{
    private readonly RequestSqlMetrics _metrics;

    public SqlCommandMetricsInterceptor(RequestSqlMetrics metrics) => _metrics = metrics;

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        _metrics.Record(eventData.Duration);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        _metrics.Record(eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        _metrics.Record(eventData.Duration);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        _metrics.Record(eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        _metrics.Record(eventData.Duration);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        _metrics.Record(eventData.Duration);
        return ValueTask.FromResult(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData) =>
        _metrics.Record(eventData.Duration);

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _metrics.Record(eventData.Duration);
        return Task.CompletedTask;
    }
}
