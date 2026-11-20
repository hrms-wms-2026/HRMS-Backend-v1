using Microsoft.Extensions.Configuration;
using ONEVO.Application.Common.Tenancy;

namespace ONEVO.Tests.Unit.Features.Api.Tenancy;

public class TenancyModeTests
{
    private static IConfiguration Config(string? mode)
    {
        var values = new Dictionary<string, string?>();
        if (mode is not null) values["Tenancy:Mode"] = mode;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Read_Missing_ReturnsSubdomain()
        => Assert.Equal(TenancyMode.Subdomain, TenancyModeReader.Read(Config(null)));

    [Fact]
    public void Read_SingleHost_CaseInsensitive()
        => Assert.Equal(TenancyMode.SingleHost, TenancyModeReader.Read(Config("singlehost")));

    [Fact]
    public void Read_Subdomain_ReturnsSubdomain()
        => Assert.Equal(TenancyMode.Subdomain, TenancyModeReader.Read(Config("Subdomain")));

    [Fact]
    public void Read_Unknown_Throws()
        => Assert.Throws<InvalidOperationException>(() => TenancyModeReader.Read(Config("Path")));
}
