using System.Text.Json;
using FluentAssertions;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

/// <summary>The admin console sends and reads the provider as "crownusAppProvider".</summary>
public sealed class IntegrationCatalogContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CreateRequest_ReadsCrownusAppProvider()
    {
        var request = JsonSerializer.Deserialize<CreateIntegrationCatalogRequest>(
            """{"integrationKey":"github","crownusAppProvider":"github"}""", Web)!;

        request.CrownusAppProvider.Should().Be("github");
    }

    [Fact]
    public void UpdateRequest_ReadsCrownusAppProvider()
    {
        var request = JsonSerializer.Deserialize<UpdateIntegrationCatalogRequest>(
            """{"crownusAppProvider":"github"}""", Web)!;

        request.CrownusAppProvider.Should().Be("github");
    }

    [Fact]
    public void MissingProvider_ErrorNamesCrownusAppProvider()
    {
        typeof(IntegrationCatalogRules).Assembly.Should().NotBeNull();
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "src", "ONEVO.Application", "Features", "DevPlatform", "SystemConfig",
            "IntegrationCatalog", "Helpers", "IntegrationCatalogRules.cs"));

        source.Should().Contain("\"crownusAppProvider is required.\"");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "ONEVO.Api")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
