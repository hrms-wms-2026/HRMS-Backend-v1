using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.Persistence.Diagnostics;
using ONEVO.Infrastructure.Persistence.Interceptors;
using Xunit;

namespace ONEVO.Tests.Unit.Persistence;

public class SqlCommandMetricsInterceptorTests
{
    private sealed class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<Row> Rows => Set<Row>();
    }

    private static (TestDbContext Db, SqliteConnection Connection) CreateDb(RequestSqlMetrics metrics)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new SqlCommandMetricsInterceptor(metrics))
            .Options;
        var db = new TestDbContext(options);
        db.Database.EnsureCreated();
        return (db, connection);
    }

    [Fact]
    public void New_metrics_start_at_zero()
    {
        var metrics = new RequestSqlMetrics();

        metrics.CommandCount.Should().Be(0);
        metrics.TotalElapsed.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Counts_every_reader_scalar_and_non_query_command()
    {
        var metrics = new RequestSqlMetrics();
        var (db, connection) = CreateDb(metrics);
        await using var conn = connection;
        await using var ctx = db;
        var before = metrics.CommandCount;

        db.Rows.Add(new Row { Name = "a" });
        await db.SaveChangesAsync();                           // insert
        _ = await db.Rows.ToListAsync();                       // reader
        _ = await db.Rows.CountAsync();                        // scalar via reader
        _ = db.Rows.Where(r => r.Name == "a").ToList();       // sync reader
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Rows WHERE Id = -1"); // non-query

        (metrics.CommandCount - before).Should().Be(5);
        metrics.TotalElapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task Failed_commands_are_counted_too()
    {
        var metrics = new RequestSqlMetrics();
        var (db, connection) = CreateDb(metrics);
        await using var conn = connection;
        await using var ctx = db;
        var before = metrics.CommandCount;

        var act = () => db.Database.ExecuteSqlRawAsync("SELECT * FROM missing_table");

        await act.Should().ThrowAsync<SqliteException>();
        (metrics.CommandCount - before).Should().Be(1);
    }

    [Fact]
    public void Record_is_safe_from_many_threads()
    {
        var metrics = new RequestSqlMetrics();

        Parallel.For(0, 1000, _ => metrics.Record(TimeSpan.FromTicks(10)));

        metrics.CommandCount.Should().Be(1000);
        metrics.TotalElapsed.Should().Be(TimeSpan.FromTicks(10_000));
    }
}
