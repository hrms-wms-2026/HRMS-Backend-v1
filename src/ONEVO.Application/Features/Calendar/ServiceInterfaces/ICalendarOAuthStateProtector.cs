namespace ONEVO.Application.Features.Calendar.ServiceInterfaces;

public sealed record CalendarOAuthState(
    string Nonce,
    Guid TenantId,
    Guid UserId,
    string Provider,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public interface ICalendarOAuthStateProtector
{
    string Protect(CalendarOAuthState state);
    bool TryUnprotect(string protectedState, out CalendarOAuthState? state);
}
