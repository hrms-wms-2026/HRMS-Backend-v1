namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Reloads the database rate limit overrides straight away on this API instance (task 46). The
/// admin commands (task 47) call it after they save a change; other instances pick the change up
/// within their 30-second check.
/// </summary>
public interface IRateLimitSettingsInvalidator
{
    Task InvalidateAsync(CancellationToken ct = default);
}
