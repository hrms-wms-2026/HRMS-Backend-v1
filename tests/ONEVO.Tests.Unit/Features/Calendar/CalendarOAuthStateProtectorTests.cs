using Microsoft.AspNetCore.DataProtection;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Infrastructure.Security;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarOAuthStateProtectorTests
{
    private static ICalendarOAuthStateProtector BuildSut()
    {
        var provider = DataProtectionProvider.Create("ONEVO.Tests.CalendarOAuth");
        return new CalendarOAuthStateProtector(provider);
    }

    [Fact]
    public void Protect_ThenUnprotect_RoundTripsTheState()
    {
        var sut = BuildSut();
        var state = new CalendarOAuthState("nonce-1", Guid.NewGuid(), Guid.NewGuid(), "google", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10));

        var protectedState = sut.Protect(state);
        var ok = sut.TryUnprotect(protectedState, out var result);

        Assert.True(ok);
        Assert.Equal(state, result);
    }

    [Fact]
    public void TryUnprotect_TamperedPayload_ReturnsFalse()
    {
        var sut = BuildSut();
        var state = new CalendarOAuthState("nonce-1", Guid.NewGuid(), Guid.NewGuid(), "google", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10));
        var protectedState = sut.Protect(state);
        var tampered = protectedState[..^2] + "xx";

        var ok = sut.TryUnprotect(tampered, out var result);

        Assert.False(ok);
        Assert.Null(result);
    }

    [Fact]
    public void TryUnprotect_ProtectedByADifferentProtector_ReturnsFalse()
    {
        var sut = BuildSut();
        var otherProvider = DataProtectionProvider.Create("ONEVO.Tests.SomeOtherPurpose");
        var otherProtector = new CalendarOAuthStateProtector(otherProvider);
        var state = new CalendarOAuthState("nonce-1", Guid.NewGuid(), Guid.NewGuid(), "google", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10));
        var protectedByOther = otherProtector.Protect(state);

        var ok = sut.TryUnprotect(protectedByOther, out var result);

        Assert.False(ok);
        Assert.Null(result);
    }
}
