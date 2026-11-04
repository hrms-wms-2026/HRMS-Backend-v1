using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ONEVO.Api.Extensions;

public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// X-Forwarded-* is honoured only from trusted proxies: loopback by default, plus whatever
    /// ForwardedHeaders:KnownProxies (IPs) and ForwardedHeaders:KnownNetworks (CIDRs) add for the
    /// real load balancer. The trust list is never cleared - X-Forwarded-Host picks the tenant
    /// (HostTenantResolutionMiddleware) and X-Forwarded-For keys the rate limiters, so trusting
    /// any caller would let a client choose both. Forwarded hosts are further limited to the
    /// tenant root domain and its subdomains when Tenancy:RootDomain is set.
    /// </summary>
    public static IServiceCollection AddApiForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("ForwardedHeaders");
        var knownProxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];
        var knownNetworks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];
        var rootDomain = configuration["Tenancy:RootDomain"];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedHost
                | ForwardedHeaders.XForwardedProto;

            foreach (var proxy in knownProxies)
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            foreach (var network in knownNetworks)
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));

            if (!string.IsNullOrWhiteSpace(rootDomain))
            {
                options.AllowedHosts.Add(rootDomain);
                options.AllowedHosts.Add("*." + rootDomain);
            }
        });

        return services;
    }
}
