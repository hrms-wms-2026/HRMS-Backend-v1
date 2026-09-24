namespace ONEVO.Domain.Features.Monitoring.CheckIn.Entities;

public static class MonitoringFaceScanStatus
{
    public const string PendingScan     = "pending_scan";
    public const string Available       = "available";
    public const string Failed          = "failed";
    public const string Verified        = "verified";
    public const string NotMatched      = "not_matched";
    public const string NoReferencePhoto = "no_reference_photo";
}
