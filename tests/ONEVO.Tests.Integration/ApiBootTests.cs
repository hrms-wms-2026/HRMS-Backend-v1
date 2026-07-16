using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ONEVO.Tests.Integration;

public class ApiBootTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiBootTests(WebApplicationFactory<Program> factory)
        => _factory = factory;

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.True(
            response.IsSuccessStatusCode,
            $"Health endpoint returned {response.StatusCode}");
    }

    [Fact]
    public async Task SwaggerEndpoint_ReturnsOk_InDevelopment()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.True(
            response.IsSuccessStatusCode,
            $"Swagger endpoint returned {response.StatusCode}");
    }
}
