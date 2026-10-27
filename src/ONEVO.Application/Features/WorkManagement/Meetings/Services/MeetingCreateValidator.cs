using System.Net.Mail;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>The input rules every create path shares (spec 5.2), each failure carrying its §7 code.</summary>
public sealed class MeetingCreateValidator
{
    public const int MaxTitleLength = 500;
    /// <summary>The meeting_series / meeting_details column widths.</summary>
    public const int MaxLocationLength = 500;
    public const int MaxRecurrenceRuleLength = 500;

    private readonly IMeetingRecurrence _recurrence;

    public MeetingCreateValidator(IMeetingRecurrence recurrence) => _recurrence = recurrence;

    public Result Validate(CreateMeetingRequest request, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > MaxTitleLength)
            return Result.Failure($"A meeting needs a title of 1-{MaxTitleLength} characters.", 400, MeetingErrorCodes.TitleInvalid);
        if (request.DurationMinutes <= 0)
            return Result.Failure("A meeting must end after it starts.", 400, MeetingErrorCodes.InvalidTime);
        if (request.Start <= now)
            return Result.Failure("A meeting must start in the future.", 400, MeetingErrorCodes.StartInPast);

        if (CheckLengths(request.Location, request.RecurrenceRule) is { } tooLong)
            return tooLong;

        var recurrence = _recurrence.Validate(request.RecurrenceRule, request.Start, request.Timezone);
        if (!recurrence.IsSuccess)
            return recurrence;

        if (request.Platform is not null && !ONEVO.Application.Features.Calendar.Services.CalendarMeetingConnections.IsKnownProvider(request.Platform))
            return Result.Failure("Choose Microsoft Teams or Zoom.", 400, MeetingErrorCodes.PlatformInvalid);

        foreach (var email in request.GuestEmails)
            if (!IsEmail(email))
                return Result.Failure($"'{email}' is not a valid guest email.", 400, MeetingErrorCodes.GuestEmailInvalid);

        return Result.Success();
    }

    /// <summary>400 when the location or the recurrence rule would not fit its column (both paths: create and edit).</summary>
    public static Result? CheckLengths(string? location, string? recurrenceRule)
    {
        if (location is not null && location.Trim().Length > MaxLocationLength)
            return Result.Failure($"A location can be at most {MaxLocationLength} characters.", 400, MeetingErrorCodes.LocationInvalid);
        if (recurrenceRule is not null && recurrenceRule.Length > MaxRecurrenceRuleLength)
            return Result.Failure($"A repeat rule can be at most {MaxRecurrenceRuleLength} characters.", 400, MeetingErrorCodes.RecurrenceInvalid);
        return null;
    }

    /// <summary>Trimmed, with duplicates removed case-insensitively; the first spelling is kept.</summary>
    public static IReadOnlyList<string> NormalizeGuests(IEnumerable<string> emails)
        => emails.Select(e => e.Trim()).Where(e => e.Length > 0)
            .DistinctBy(e => e.ToLowerInvariant())
            .ToList();

    private static bool IsEmail(string value)
        => MailAddress.TryCreate(value.Trim(), out var address) && address.Address == value.Trim() && value.Contains('@');
}
