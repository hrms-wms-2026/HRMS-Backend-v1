namespace ONEVO.Application.Features.Leave.Cancellation.Options;

public sealed class LeaveCancellationOptions
{
    public const string SectionName = "Leave:Cancellation";

    public string? FallbackTimezone { get; init; }

    public bool RequireEmployeeReason { get; init; }

    public static bool IsValidTimezone(string? timezone)
        => ResolveTimezone(timezone) is not null;

    public static TimeZoneInfo? ResolveTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
            return null;

        var trimmed = timezone.Trim();
        if (TryFind(trimmed, out var direct))
            return direct;

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(trimmed, out var windowsId)
            && TryFind(windowsId, out var windows))
        {
            return windows;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(trimmed, out var ianaId)
            && TryFind(ianaId, out var iana))
        {
            return iana;
        }

        return null;
    }

    private static bool TryFind(string timezoneId, out TimeZoneInfo? zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
            return zone is not null;
        }
        catch (TimeZoneNotFoundException)
        {
            zone = null;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            zone = null;
            return false;
        }
    }
}
