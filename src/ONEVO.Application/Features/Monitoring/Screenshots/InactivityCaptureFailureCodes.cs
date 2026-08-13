namespace ONEVO.Application.Features.Monitoring.Screenshots;

/// <summary>Known stable failure codes for <see cref="InactivityCaptureOutcomes.CaptureFailed"/> attempts.</summary>
public static class InactivityCaptureFailureCodes
{
    public const string SessionLocked = "session_locked";
    public const string NoDisplays = "no_displays";
    public const string ZeroBounds = "zero_bounds";
    public const string CaptureApiFailed = "capture_api_failed";
    public const string CaptureTooLarge = "capture_too_large";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SessionLocked,
        NoDisplays,
        ZeroBounds,
        CaptureApiFailed,
        CaptureTooLarge
    };
}
