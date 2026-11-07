using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Jobs;

/// <summary>
/// <see cref="IJobLeaseService"/> over job_leases. Every time comparison uses the database clock (now()), so instances
/// with skewed clocks agree on expiry. job_leases has no RLS, so no tenant context is needed. Singleton: each call opens
/// its own scope and short transaction.
/// </summary>
public sealed class JobLeaseService : IJobLeaseService
{
    private readonly IServiceScopeFactory _scopes;

    public JobLeaseService(IServiceScopeFactory scopes, string? instanceId = null)
    {
        _scopes = scopes;
        InstanceId = instanceId ?? BuildInstanceId(Environment.MachineName, Environment.ProcessId, Guid.NewGuid());
    }

    /// <summary>"{host}:{pid}:{guid}" within owner_instance_id's 100 characters: host names (63 in Kubernetes) are cut
    /// to 40; the guid alone keeps the id unique.</summary>
    public static string BuildInstanceId(string machineName, int processId, Guid id)
        => $"{(machineName.Length > 40 ? machineName[..40] : machineName)}:{processId}:{id:N}";

    public async Task<bool> RenewAsync(string jobName, TimeSpan ttl, CancellationToken ct)
    {
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "A lease must last a positive time.");

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var renewed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE job_leases
            SET lease_until = DATEADD(millisecond, {(int)ttl.TotalMilliseconds}, SYSDATETIMEOFFSET()),
                heartbeat_at = SYSDATETIMEOFFSET()
            WHERE job_name = {jobName} AND owner_instance_id = {InstanceId} AND lease_until >= SYSDATETIMEOFFSET()
            """, ct);
        return renewed == 1;
    }

    public string InstanceId { get; }

    public async Task<bool> TryAcquireAsync(string jobName, TimeSpan ttl, CancellationToken ct)
    {
        if (ttl <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ttl), "A lease must last a positive time.");

        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // The row exists once per job; a new row starts expired so the UPDATE below decides ownership in one place.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO job_leases (job_name, owner_instance_id, lease_until, heartbeat_at)
            SELECT {jobName}, N'', '0001-01-01T00:00:00+00:00', SYSDATETIMEOFFSET()
            WHERE NOT EXISTS (SELECT 1 FROM job_leases WITH (UPDLOCK, HOLDLOCK) WHERE job_name = {jobName})
            """, ct);
        var acquired = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE job_leases
            SET owner_instance_id = {InstanceId},
                lease_until = DATEADD(millisecond, {(int)ttl.TotalMilliseconds}, SYSDATETIMEOFFSET()),
                heartbeat_at = SYSDATETIMEOFFSET()
            WHERE job_name = {jobName} AND (lease_until < SYSDATETIMEOFFSET() OR owner_instance_id = {InstanceId})
            """, ct);

        await tx.CommitAsync(ct);
        return acquired == 1;
    }

    public async Task ReleaseAsync(string jobName, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE job_leases SET lease_until = '0001-01-01T00:00:00+00:00', heartbeat_at = SYSDATETIMEOFFSET()
            WHERE job_name = {jobName} AND owner_instance_id = {InstanceId}
            """, ct);
    }
}
