using ONEVO.Application.Common.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Fakes;

public sealed class FakeDateTimeProvider : IDateTimeProvider
{
    private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;

    public DateTimeOffset UtcNow
    {
        get => _utcNow;
        set => _utcNow = value;
    }

    public DateOnly Today => DateOnly.FromDateTime(_utcNow.Date);
}
