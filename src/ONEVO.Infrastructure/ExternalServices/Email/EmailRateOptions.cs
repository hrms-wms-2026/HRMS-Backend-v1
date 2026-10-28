namespace ONEVO.Infrastructure.ExternalServices.Email;

/// <summary>
/// Bound from the <c>Email:*</c> section of configuration (Plan D Task 3).
/// <para>
/// <see cref="MaxPerSecond"/> paces outbound email: at most that many sends per rolling second, per app
/// instance. <c>0</c> means unlimited, which is the default - the limit is a deployment setting, not a code
/// default, because only the operator knows the provider's rate limit and the instance count.
/// </para>
/// </summary>
public class EmailRateOptions
{
    public const string SectionName = "Email";

    /// <summary>At most this many sends per rolling second per instance. 0 = unlimited.</summary>
    public int MaxPerSecond { get; set; }
}
