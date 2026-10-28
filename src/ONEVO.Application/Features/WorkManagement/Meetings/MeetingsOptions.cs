using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings;

/// <summary>
/// "Meetings" configuration (meetings-as-tasks Phase 2 spec 8). Every flag defaults to off: Release A ships the schema
/// and primitives with no behaviour switched on. ReadOnly keeps meeting data visible while blocking every write - the
/// recovery switch, since a rollback below Release A is never allowed.
/// </summary>
public sealed class MeetingsOptions
{
    public const string Section = "Meetings";

    public bool Create { get; init; }
    public bool BulkInvite { get; init; }
    public bool Background { get; init; }
    public bool ReadOnly { get; init; }
    public MeetingAttendanceOptions Attendance { get; init; } = new();

    /// <summary>
    /// The provider's meeting size, per provider - above it the organizer sees a warning (spec 5.2, D8).
    /// A warning, never a cap: the organizer may still invite everyone. The keys are the configuration
    /// keys "MicrosoftTeams" and "Zoom"; the lookup is case-insensitive.
    /// </summary>
    public IReadOnlyDictionary<string, int> ProviderCapacityWarning { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [MeetingProviderCapacityKeys.MicrosoftTeams] = 1000,
            [MeetingProviderCapacityKeys.Zoom] = 300,
        };

    /// <summary>Binds the "Meetings" section onto <paramref name="target"/>. A configured checkpoint list replaces the
    /// default one: the configuration binder alone would append it to the defaults.</summary>
    public static void BindFrom(IConfigurationSection meetings, MeetingsOptions target)
    {
        meetings.Bind(target);
        var configured = meetings.GetSection($"{nameof(Attendance)}:{nameof(MeetingAttendanceOptions.Checkpoints)}").Get<TimeSpan[]>();
        if (configured is { Length: > 0 })
            target.Attendance.Checkpoints = configured;
    }
}

/// <summary>
/// The keys of "Meetings:ProviderCapacityWarning": "MicrosoftTeams" and "Zoom". A provider constant
/// ("microsoft_teams", "zoom") is mapped onto its configuration key; the lookup accepts either spelling.
/// </summary>
public static class MeetingProviderCapacityKeys
{
    public const string MicrosoftTeams = "MicrosoftTeams";
    public const string Zoom = "Zoom";

    /// <summary>The configuration key for a provider ("microsoft_teams" → "MicrosoftTeams").</summary>
    public static string For(string provider) => provider switch
    {
        CalendarEventMeetingProviders.MicrosoftTeams => MicrosoftTeams,
        CalendarEventMeetingProviders.Zoom => Zoom,
        _ => provider,
    };
}

/// <summary>"Meetings:Attendance" (spec 5.8).</summary>
public sealed class MeetingAttendanceOptions
{
    /// <summary>When attendance is checked after an occurrence ends; strictly increasing, all positive.</summary>
    /// <remarks>Settable (not init) so <see cref="MeetingsOptions.BindFrom"/> can replace the defaults.</remarks>
    public TimeSpan[] Checkpoints { get; set; } = [TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(24)];

    /// <summary>How long before an occurrence's start a provider session still counts as that occurrence.</summary>
    public TimeSpan MatchWindowBefore { get; init; } = TimeSpan.FromHours(1);

    /// <summary>How long after an occurrence the organizer may still ask for a recheck.</summary>
    public TimeSpan LateRecheckWindow { get; init; } = TimeSpan.FromDays(7);

    /// <summary>The one automatic retry after a failed sync.</summary>
    public TimeSpan FailedRetryAfter { get; init; } = TimeSpan.FromDays(3);
}

/// <summary>Startup validation of <see cref="MeetingsOptions"/>; every message names the configuration key to fix.</summary>
public sealed class MeetingsOptionsValidator : IValidateOptions<MeetingsOptions>
{
    private const string Attendance = MeetingsOptions.Section + ":Attendance:";

    public ValidateOptionsResult Validate(string? name, MeetingsOptions options)
    {
        var failures = new List<string>();
        var attendance = options.Attendance;

        if (attendance.Checkpoints is not { Length: > 0 } checkpoints)
        {
            failures.Add($"{Attendance}{nameof(MeetingAttendanceOptions.Checkpoints)} must list at least one checkpoint.");
        }
        else
        {
            if (checkpoints[0] <= TimeSpan.Zero)
                failures.Add($"{Attendance}{nameof(MeetingAttendanceOptions.Checkpoints)} must all be positive.");
            for (var i = 1; i < checkpoints.Length; i++)
            {
                if (checkpoints[i] <= checkpoints[i - 1])
                {
                    failures.Add($"{Attendance}{nameof(MeetingAttendanceOptions.Checkpoints)} must be strictly increasing.");
                    break;
                }
            }
        }

        RequirePositive(attendance.MatchWindowBefore, nameof(MeetingAttendanceOptions.MatchWindowBefore));
        RequirePositive(attendance.LateRecheckWindow, nameof(MeetingAttendanceOptions.LateRecheckWindow));
        RequirePositive(attendance.FailedRetryAfter, nameof(MeetingAttendanceOptions.FailedRetryAfter));

        foreach (var (provider, limit) in options.ProviderCapacityWarning)
            if (limit <= 0)
                failures.Add($"{MeetingsOptions.Section}:{nameof(MeetingsOptions.ProviderCapacityWarning)}:{provider} must be positive.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);

        void RequirePositive(TimeSpan value, string key)
        {
            if (value <= TimeSpan.Zero)
                failures.Add($"{Attendance}{key} must be positive.");
        }
    }
}
