using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Monitoring.Policy;

/// <summary>
/// Deterministic <see cref="IDateTimeProvider"/> pinned to a fixed instant, used so
/// tests can assert exact arithmetic (e.g. ValidUntil = clock + 1 hour) instead of
/// tolerating drift from a wall-clock fake.
/// </summary>
public sealed class FixedDateTimeProvider : IDateTimeProvider
{
    public FixedDateTimeProvider(DateTimeOffset utcNow) => UtcNow = utcNow;

    public DateTimeOffset UtcNow { get; }
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
