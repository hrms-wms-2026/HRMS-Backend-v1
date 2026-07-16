namespace ONEVO.Application.Common.ServiceInterfaces;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today { get; }
}
