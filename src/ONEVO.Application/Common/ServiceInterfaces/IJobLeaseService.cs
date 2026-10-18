namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// A database lease that lets exactly one app instance at a time run a singleton background job (SCL-01; meetings-as-tasks
/// Phase 2 spec 4.1). A holder renews by acquiring again before the lease runs out; an expired lease is taken over by
/// whichever instance asks next, and its old holder can no longer renew it.
/// </summary>
public interface IJobLeaseService
{
    /// <summary>True when this instance now holds <paramref name="jobName"/> until now + <paramref name="ttl"/>
    /// (newly acquired or renewed); false while another instance holds an unexpired lease.</summary>
    Task<bool> TryAcquireAsync(string jobName, TimeSpan ttl, CancellationToken ct);

    /// <summary>Ends the lease at once so another instance can take it; does nothing unless this instance holds it.</summary>
    Task ReleaseAsync(string jobName, CancellationToken ct);

    /// <summary>"{MachineName}:{ProcessId}:{Guid}", fixed for the life of the process.</summary>
    string InstanceId { get; }
}
