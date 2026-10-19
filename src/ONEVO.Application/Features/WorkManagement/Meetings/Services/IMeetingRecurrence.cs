using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>Meeting recurrence in the series' own IANA timezone, so a weekly 09:00 stays 09:00 local across DST
/// (spec 5.2). Instants are returned in UTC.</summary>
public interface IMeetingRecurrence
{
    public const int MaxOccurrences = 500;

    /// <summary>Occurrence starts in [from, to). A null rule is a one-off meeting: [dtStart] when it lies in range.</summary>
    IReadOnlyList<DateTimeOffset> Occurrences(string? rule, DateTimeOffset dtStart, string timezone, DateTimeOffset from, DateTimeOffset to);

    /// <summary>meeting_invalid_timezone for an unknown zone; meeting_recurrence_invalid for an unparsable rule, one that
    /// never fires, a COUNT above 500, or more than 500 occurrences in its first year (meetings recur at most daily-ish).</summary>
    Result Validate(string? rule, DateTimeOffset dtStart, string timezone);
}
