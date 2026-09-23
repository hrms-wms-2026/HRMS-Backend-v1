namespace ONEVO.Application.Features.Calendar.Services;

public interface ICalendarTimezoneResolver
{
    /// <summary>Resolves the effective IANA timezone for one user: their own DisplayTimezone if
    /// set, else their legal entity's Timezone, else "UTC" - the same order GetMyProfileQueryHandler
    /// already uses. Returns "UTC" if the user has no employee record at all.</summary>
    Task<string> ResolveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
