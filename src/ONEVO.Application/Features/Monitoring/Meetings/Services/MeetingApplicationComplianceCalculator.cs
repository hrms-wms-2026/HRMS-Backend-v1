namespace ONEVO.Application.Features.Monitoring.Meetings.Services;

public sealed record MeetingApplicationUsageResult(
    int RequiredSeconds,
    int UsedSeconds,
    decimal UsagePercentage,
    bool IsCompliant);

public static class MeetingApplicationComplianceCalculator
{
    public const int AppUsageSampleSeconds = 60;
    public const decimal RequiredUsageRatio = 0.50m;

    public static MeetingApplicationUsageResult Calculate(int durationSeconds, int matchingSamples)
    {
        if (durationSeconds <= 0)
            return new MeetingApplicationUsageResult(0, 0, 0m, false);

        var requiredSeconds = (int)Math.Ceiling(durationSeconds * RequiredUsageRatio);
        var usedSeconds = Math.Min(durationSeconds, Math.Max(0, matchingSamples) * AppUsageSampleSeconds);
        var percentage = Math.Round((decimal)usedSeconds / durationSeconds * 100m, 2);
        return new MeetingApplicationUsageResult(
            requiredSeconds,
            usedSeconds,
            percentage,
            usedSeconds >= requiredSeconds);
    }
}
