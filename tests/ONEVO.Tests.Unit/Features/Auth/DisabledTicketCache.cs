using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.Sessions;

namespace ONEVO.Tests.Unit.Features.Auth;

/// <summary>A ticket cache with caching and renewal throttling switched off, for tests that cover
/// the ticket stores' database path one call at a time.</summary>
internal static class DisabledTicketCache
{
    public static SessionTicketCache Create(IDateTimeProvider clock) =>
        new(new MemoryCache(new MemoryCacheOptions()), clock, Options.Create(new SessionTicketCacheOptions
        {
            LifetimeSeconds = 0,
            RenewWriteIntervalSeconds = 0,
        }));
}
