namespace ONEVO.Application.Features.Monitoring.Exceptions.Services;

/// <summary>A legal entity's configured timezone, falling back to UTC when it is unset or unknown -
/// the same fallback AttendanceReadHandler uses for the attendance day window.</summary>
public static class CompanyTimeZone
{
    public static TimeZoneInfo Find(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return TimeZoneInfo.Utc;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>Start of the company-local day that <paramref name="at"/> falls on, as a UTC instant.</summary>
    public static DateTimeOffset StartOfDay(DateTimeOffset at, TimeZoneInfo zone)
    {
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
        var localMidnight = localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localMidnight, zone), TimeSpan.Zero);
    }
}
