using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Fakes;

public sealed class FakeDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
