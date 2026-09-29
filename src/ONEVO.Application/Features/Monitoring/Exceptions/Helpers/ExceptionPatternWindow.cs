using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Helpers;

/// <summary>
/// Which days the evidence shows for a nightly multi-day case, and which of those the detection
/// rule itself evaluated - kept next to ExceptionDetectionRules so the two can't drift apart.
/// </summary>
/// <param name="From">First day shown (includes context before the rule's own days).</param>
/// <param name="RuleFrom">First day the rule looked at.</param>
public sealed record ExceptionPatternWindow(DateOnly From, DateOnly To, DateOnly RuleFrom, DateOnly RuleTo)
{
    public bool InRule(DateOnly date) => date >= RuleFrom && date <= RuleTo;

    /// <summary>Null for types that are not multi-day patterns (identity cases).</summary>
    public static ExceptionPatternWindow? For(ExceptionType type, DateOnly target) => type switch
    {
        // The last 3 days, each below the score threshold; a week shown for context.
        ExceptionType.SustainedLowActivity => new ExceptionPatternWindow(
            target.AddDays(-6), target,
            target.AddDays(-(ExceptionDetectionRules.SustainedLowActivityConsecutiveDays - 1)), target),
        // This week's worked time against the 4 weeks before it; two weeks shown.
        ExceptionType.AttendanceIrregularity => new ExceptionPatternWindow(
            target.AddDays(-13), target, target.AddDays(-6), target),
        // One day against the 30 before it; two weeks shown.
        ExceptionType.UnusualActivityPattern => new ExceptionPatternWindow(
            target.AddDays(-13), target, target, target),
        _ => null
    };
}
