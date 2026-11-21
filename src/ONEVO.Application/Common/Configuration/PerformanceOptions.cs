namespace ONEVO.Application.Common.Configuration;

/// <summary>
/// Performance thresholds, bound from the "Performance" config section.
/// </summary>
public class PerformanceOptions
{
    public const string SectionName = "Performance";

    /// <summary>
    /// A request or MediatR handler slower than this is logged as a warning.
    /// The team target is 200 ms per API call (Improvements task 06).
    /// </summary>
    public int SlowRequestThresholdMs { get; set; } = 200;
}
