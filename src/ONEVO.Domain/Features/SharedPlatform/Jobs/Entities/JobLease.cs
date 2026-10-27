namespace ONEVO.Domain.Features.SharedPlatform.Jobs.Entities;

/// <summary>
/// A platform-wide lease that lets one app instance run a singleton background job at a time (meetings-as-tasks
/// Phase 2 spec 4.1). Not tenant-owned: no tenant filter, no RLS.
/// </summary>
public class JobLease
{
    public string JobName { get; set; } = string.Empty;
    public string OwnerInstanceId { get; set; } = string.Empty;
    public DateTimeOffset LeaseUntil { get; set; }
    public DateTimeOffset HeartbeatAt { get; set; }
}
