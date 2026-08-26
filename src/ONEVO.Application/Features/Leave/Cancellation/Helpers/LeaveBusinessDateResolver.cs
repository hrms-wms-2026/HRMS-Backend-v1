using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Cancellation.Options;

namespace ONEVO.Application.Features.Leave.Cancellation.Helpers;

public sealed class LeaveBusinessDateResolver
{
    private readonly IDateTimeProvider _clock;
    private readonly LeaveCancellationOptions _options;

    public LeaveBusinessDateResolver(
        IDateTimeProvider clock,
        IOptions<LeaveCancellationOptions> options)
    {
        _clock = clock;
        _options = options.Value;
    }

    public DateOnly Today(string? legalEntityTimezone)
    {
        var timezoneId = string.IsNullOrWhiteSpace(legalEntityTimezone)
            ? _options.FallbackTimezone!
            : legalEntityTimezone.Trim();

        var zone = LeaveCancellationOptions.ResolveTimezone(timezoneId)!;
        var local = TimeZoneInfo.ConvertTime(_clock.UtcNow, zone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
