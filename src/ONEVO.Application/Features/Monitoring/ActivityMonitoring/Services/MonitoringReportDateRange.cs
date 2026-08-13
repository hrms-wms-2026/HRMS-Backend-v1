namespace ONEVO.Application.Features.Monitoring.ActivityMonitoring.Services;

/// <summary>Converts a local calendar date to UTC bounds for repository filters.</summary>
public static class MonitoringReportDateRange
{
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ToUtcBounds(
        DateOnly localDate,
        TimeZoneInfo timeZone)
    {
        var localStart = localDate.ToDateTime(TimeOnly.MinValue);
        var localEnd = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var fromUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone));
        var toUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone));
        return (fromUtc, toUtc);
    }

    public static DateOnly ToLocalDate(DateTimeOffset instantUtc, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(instantUtc, timeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
