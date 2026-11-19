using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;
using ONEVO.Application.Features.TimeAttendance.Team.Services;
using ONEVO.Application.Features.WorkManagement.Monitoring.Services;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.TimeAttendance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Monitoring;

public class ConversionRateReaderTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset UtcNow = DateTimeOffset.Parse("2026-11-08T06:00:00+00:00"); // Sun 11:30 Colombo
    private static readonly DateOnly Today = new(2026, 11, 8);
    private static readonly DateOnly WindowStart = new(2026, 10, 11);
    private static readonly TimeSpan Colombo = TimeSpan.FromHours(5.5);
    private static readonly LegalEntity Entity = new() { Id = Guid.NewGuid(), TenantId = TenantId, Timezone = "Asia/Colombo" };

    private readonly Mock<IAttendanceReadRepository> _attendance = new();
    private readonly Mock<ITeamCompletedTaskReader> _tasks = new();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private List<AttendanceRecord> _records = [];
    private List<TeamCompletedTask> _completed = [];

    public ConversionRateReaderTests()
    {
        _clock.SetupGet(x => x.UtcNow).Returns(UtcNow);
        _clock.SetupGet(x => x.Today).Returns(Today);
        _attendance.Setup(x => x.ListRecordsAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> _, DateOnly from, DateOnly to, int _, int _, CancellationToken _) =>
            {
                var rows = _records.Where(r => r.Date >= from && r.Date <= to).ToList();
                return (rows, rows.Count);
            });
        _tasks.Setup(x => x.ListCompletedAsync(TenantId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyCollection<Guid> _, DateTimeOffset from, DateTimeOffset to, CancellationToken _) =>
                _completed.Where(t => t.CompletedAt >= from && t.CompletedAt < to).ToList());
    }

    private ConversionRateReader Reader() => new(_attendance.Object, _tasks.Object, _clock.Object);

    private static AttendanceRecord Record(Guid employeeId, DateOnly date, int worked) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, Date = date, WorkedMinutes = worked, BreakMinutes = 0,
        ActualStart = new DateTimeOffset(date.ToDateTime(new TimeOnly(3, 30)), TimeSpan.Zero),
        ActualEnd = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 30)), TimeSpan.Zero),
    };

    private static TeamCompletedTask Done(Guid employeeId, DateOnly date, decimal hours)
        => new(Guid.NewGuid(), employeeId, hours, new DateTimeOffset(date.ToDateTime(new TimeOnly(14, 0)), Colombo));

    [Fact]
    public async Task Reads_only_the_last_28_days_and_buckets_them_by_week()
    {
        var a = Guid.NewGuid();
        _records =
        [
            Record(a, WindowStart.AddDays(-1), 600),   // before the window: ignored
            Record(a, WindowStart, 480),               // week 0
            Record(a, WindowStart.AddDays(27), 480),   // week 3 (yesterday)
            Record(a, Today, 480),                     // today: never read
        ];
        _completed = [Done(a, WindowStart.AddDays(-2), 8m), Done(a, WindowStart.AddDays(1), 2m), Done(a, WindowStart.AddDays(27), 4m), Done(a, Today, 8m)];

        var set = await Reader().ReadAsync(TenantId, Entity, [a]);

        Assert.Equal((Today, WindowStart, Today.AddDays(-1)), (set.Today, set.WindowStart, set.WindowEnd));
        var r = set.ByEmployee[a];
        Assert.Equal(960, r.WorkedMinutes);
        Assert.Equal(360, r.TaskMinutes);
        Assert.Equal(0.375m, r.Rate);
        Assert.Equal([480, 0, 0, 480], r.WeekWorked);
        Assert.Equal([120, 0, 0, 240], r.WeekTask);
    }

    [Fact]
    public async Task Pooled_rate_sums_minutes_instead_of_averaging_rates()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _records = [Record(a, WindowStart, 60), Record(b, WindowStart, 540)];
        _completed = [Done(a, WindowStart, 1m), Done(b, WindowStart, 2m)];   // a: 100%, b: 22%

        var set = await Reader().ReadAsync(TenantId, Entity, [a, b]);
        var pooled = set.Pool([a, b, a]);

        Assert.Equal((600, 180, 2), (pooled.WorkedMinutes, pooled.TaskMinutes, pooled.Contributors));
        Assert.Equal(0.3m, pooled.Rate);
        var trend = set.Trend([a, b]);
        Assert.Equal(4, trend.Count);
        Assert.Equal(WindowStart, trend[0].WeekStart);
        Assert.Equal(0.3m, trend[0].Rate);
        Assert.Null(trend[1].Rate);
    }

    [Fact]
    public async Task Under_an_hour_of_work_has_no_rate()
    {
        var a = Guid.NewGuid();
        _records = [Record(a, WindowStart, 45)];
        var set = await Reader().ReadAsync(TenantId, Entity, [a]);
        Assert.Null(set.RateOf(a));
        Assert.Null(set.RateOf(Guid.NewGuid()));
    }
}
