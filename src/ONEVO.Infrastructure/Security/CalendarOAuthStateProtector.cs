using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;

namespace ONEVO.Infrastructure.Security;

public sealed class CalendarOAuthStateProtector : ICalendarOAuthStateProtector
{
    private readonly IDataProtector _protector;

    public CalendarOAuthStateProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("ONEVO.CalendarOAuth.State.v1");
    }

    public string Protect(CalendarOAuthState state)
        => _protector.Protect(JsonSerializer.Serialize(state));

    public bool TryUnprotect(string protectedState, out CalendarOAuthState? state)
    {
        state = null;
        try
        {
            var json = _protector.Unprotect(protectedState);
            state = JsonSerializer.Deserialize<CalendarOAuthState>(json);
            return state is not null;
        }
        catch
        {
            return false;
        }
    }
}
