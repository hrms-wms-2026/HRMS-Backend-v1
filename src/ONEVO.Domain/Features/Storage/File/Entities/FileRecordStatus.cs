namespace ONEVO.Domain.Features.Storage.File.Entities;

public static class FileRecordStatus
{
    public const string PendingScan = "pending_scan";
    public const string Available = "available";
    public const string Quarantined = "quarantined";
    public const string Deleted = "deleted";
}
