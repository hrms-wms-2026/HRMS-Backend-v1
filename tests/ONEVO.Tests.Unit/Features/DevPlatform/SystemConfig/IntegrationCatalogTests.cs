using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class IntegrationCatalogTests
{
    [Theory]
    [InlineData("github", true)]
    [InlineData("google_calendar", true)]
    [InlineData("Google_Calendar", false)]
    [InlineData("google calendar", false)]
    [InlineData("", false)]
    public void Integration_key_validation_enforces_lowercase_slug(string key, bool expected) =>
        Assert.Equal(expected, IntegrationCatalogRules.IsValidSlug(key));

    [Theory]
    [InlineData("tenant", true)]
    [InlineData("user", true)]
    [InlineData("both", true)]
    [InlineData("employee", false)]
    [InlineData("Tenant", false)]
    [InlineData("User", false)]
    public void Connection_scope_is_strict(string scope, bool expected) =>
        Assert.Equal(expected, IntegrationCatalogRules.IsValidScope(scope));

    [Theory]
    [InlineData("slack")]
    [InlineData("slack_enterprise")]
    [InlineData("stripe")]
    [InlineData("sendgrid")]
    [InlineData("cloudflare")]
    [InlineData("biometric_terminal")]
    public void Phase_two_and_internal_services_are_rejected(string value) =>
        Assert.True(IntegrationCatalogRules.IsForbidden(value));

    [Fact]
    public void Dtos_contain_no_secret_or_token_fields()
    {
        var types = new[] { typeof(CreateIntegrationCatalogRequest), typeof(UpdateIntegrationCatalogRequest), typeof(IntegrationCatalogDto) };
        foreach (var property in types.SelectMany(x => x.GetProperties()))
        {
            var name = property.Name.ToLowerInvariant();
            Assert.DoesNotContain("secret", name);
            Assert.DoesNotContain("token", name);
            Assert.DoesNotContain("credential", name);
        }
    }
}
