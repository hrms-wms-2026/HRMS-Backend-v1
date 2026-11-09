using ONEVO.Tests.Integration.MyTeam;
using ONEVO.Tests.Integration.WorkManagement.Meetings;
using Xunit;

namespace ONEVO.Tests.Integration.SqlServerMigration;

/// <summary>
/// The PostgreSQL driver refused any timestamp that was not UTC. SQL Server's datetimeoffset keeps
/// whatever offset it is given, which would change DATEPART grouping and what clients read back,
/// so every value must still be stored as UTC.
/// </summary>
public sealed class UtcStorageTests
{
    private static readonly TimeSpan Colombo = TimeSpan.FromHours(5.5);

    [Fact]
    public async Task ATimestampWithAnOffset_IsStoredAsTheSameInstantInUtc()
    {
        var h = await MyTeamDb.CreateAsync();
        var localStart = new DateTimeOffset(2026, 11, 2, 1, 0, 0, Colombo);
        Guid seriesId;
        await using (var db = h.NewContext())
        {
            var series = h.AddSeries(db, h.AddPlacement(db), dtStart: localStart);
            series.CancelledAt = localStart;
            seriesId = series.Id;
            await db.SaveChangesAsync();
        }

        await using var connection = TestDb.CreateConnection(h.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"SELECT dt_start, cancelled_at, DATEPART(tzoffset, dt_start), DATEPART(tzoffset, cancelled_at) FROM meeting_series WHERE id = '{seriesId}'",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal(localStart, reader.GetDateTimeOffset(0));
        Assert.Equal(localStart, reader.GetDateTimeOffset(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal(0, reader.GetInt32(3));
    }
}
