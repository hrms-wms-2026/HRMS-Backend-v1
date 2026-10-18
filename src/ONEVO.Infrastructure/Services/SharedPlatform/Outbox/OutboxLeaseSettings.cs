using Microsoft.Extensions.Configuration;

namespace ONEVO.Infrastructure.Services.SharedPlatform.Outbox;

/// <summary>
/// How long a processor instance owns a claimed message, and how long its handler may run.
///
/// Lease: 5 minutes by default (Outbox:LeaseSeconds). The worst-case time to dispatch one message
/// is one provider HTTP call: the email adapters use a plain named HttpClient with the default
/// 100 s timeout and no retry handler, and template rendering / key resolution take milliseconds.
/// The lease is renewed right before each message's handler starts, so it only has to cover one
/// handler run plus the completion write, not the whole batch.
///
/// Handler budget: 80% of the lease (4 minutes by default, 2.4x the HTTP timeout). The handler is
/// cancelled when its budget runs out, so it stops while its lease is still valid and at least a
/// minute remains to record the outcome. That is what keeps a slow handler from still running when
/// another instance reclaims the message after the lease expires.
///
/// Recovery: a crashed instance's messages become claimable again once their lease passes, i.e.
/// within 5 minutes by default.
/// </summary>
public sealed record OutboxLeaseSettings(TimeSpan Lease, TimeSpan HandlerTimeout)
{
    public const string LeaseSecondsKey = "Outbox:LeaseSeconds";
    public static readonly TimeSpan DefaultLease = TimeSpan.FromMinutes(5);

    public static OutboxLeaseSettings FromConfiguration(IConfiguration configuration)
    {
        var seconds = Math.Max(1, configuration.GetValue(LeaseSecondsKey, (int)DefaultLease.TotalSeconds));
        var lease = TimeSpan.FromSeconds(seconds);
        return new OutboxLeaseSettings(lease, lease * 0.8);
    }
}
