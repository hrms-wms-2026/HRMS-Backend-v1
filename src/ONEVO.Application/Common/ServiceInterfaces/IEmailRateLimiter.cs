namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Paces outbound email. A caller awaits <see cref="WaitAsync"/> before each send; the limiter decides whether
/// that send may go now or has to wait (Plan D Task 3, <c>Email:MaxPerSecond</c>).
/// <para>
/// The pacing is per instance, not cluster-wide: each app instance keeps its own window, so the configured
/// limit has to be the provider's limit divided by the instance count. See the Phase 2 release runbook.
/// </para>
/// </summary>
public interface IEmailRateLimiter
{
    /// <summary>Waits until the next send is allowed. Returns immediately when no limit is configured.</summary>
    ValueTask WaitAsync(CancellationToken ct);
}
