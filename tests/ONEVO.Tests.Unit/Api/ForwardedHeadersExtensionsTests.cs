using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ONEVO.Api.Extensions;

namespace ONEVO.Tests.Unit.Api;

public sealed class ForwardedHeadersExtensionsTests
{
    private static ForwardedHeadersOptions Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddApiForwardedHeaders(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    [Fact]
    public void NoConfig_ForwardsProto_AndTrustsLoopbackOnly()
    {
        var options = Build([]);

        options.ForwardedHeaders.Should().HaveFlag(ForwardedHeaders.XForwardedFor)
            .And.HaveFlag(ForwardedHeaders.XForwardedHost)
            .And.HaveFlag(ForwardedHeaders.XForwardedProto);
        options.KnownProxies.Should().OnlyContain(ip => IPAddress.IsLoopback(ip));
        options.AllowedHosts.Should().BeEmpty();
    }

    [Fact]
    public void ConfiguredProxiesAndNetworks_AreAddedToLoopbackDefaults()
    {
        var options = Build(new()
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.1.0.0/16",
        });

        options.KnownProxies.Should().Contain(IPAddress.Parse("10.0.0.5"))
            .And.Contain(ip => IPAddress.IsLoopback(ip));
        options.KnownIPNetworks.Should().Contain(System.Net.IPNetwork.Parse("10.1.0.0/16"));
    }

    [Fact]
    public void RootDomain_LimitsForwardedHostsToTenantDomains()
    {
        var options = Build(new() { ["Tenancy:RootDomain"] = "onevo.com" });

        options.AllowedHosts.Should().BeEquivalentTo(["onevo.com", "*.onevo.com"]);
    }
}
