using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Services;

/// <summary>The flag and lock checks every meeting write shares (spec 7, 8).</summary>
public static class MeetingGuard
{
    /// <summary>Null when writes are allowed; otherwise 503 meetings_disabled (Create off) or 409 meetings_read_only.</summary>
    public static Result<T>? CheckWritable<T>(MeetingsOptions options)
    {
        if (!options.Create)
            return Result<T>.Failure("Meetings are not available yet.", 503, MeetingErrorCodes.MeetingsDisabled);
        if (options.ReadOnly)
            return Result<T>.Failure("Meetings are read-only right now.", 409, MeetingErrorCodes.MeetingsReadOnly);
        return null;
    }

    /// <summary>The series lock could not be taken within the 5 s lock timeout.</summary>
    public static Result<T> Busy<T>()
        => Result<T>.Failure("This meeting is being changed by someone else. Try again.", 409, MeetingErrorCodes.Busy);

    /// <summary>PostgreSQL's lock_not_available (lock_timeout expired).</summary>
    public const string LockNotAvailableSqlState = "55P03";
}
