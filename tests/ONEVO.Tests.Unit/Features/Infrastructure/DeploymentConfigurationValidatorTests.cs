using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ONEVO.Infrastructure.Configuration;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

public class DeploymentConfigurationValidatorTests
{
    private const string Secret = "deploy-validator-test-master-key-0123456789";

    private static Dictionary<string, string?> Valid() => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Server=db.uat.internal,1433;Database=Crownus;User Id=crownus_app;Password=x",
        ["Encryption:MasterKey"] = Secret,
        ["Email:FromAddress"] = "no-reply@crownus-uat.com",
        ["Tenancy:RootDomain"] = "crownus-uat.com",
        ["Urls:AppBaseUrl"] = "https://crownus-uat.com",
        ["Urls:AdminConsoleBaseUrl"] = "https://admin.crownus-uat.com",
    };

    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IReadOnlyList<string> Problems(Action<Dictionary<string, string?>> change)
    {
        var values = Valid();
        change(values);
        return DeploymentConfigurationValidator.FindProblems(Config(values));
    }

    [Fact]
    public void ValidUatSettings_HaveNoProblems()
        => DeploymentConfigurationValidator.FindProblems(Config(Valid())).Should().BeEmpty();

    [Fact]
    public void AppServiceSingleHostShape_IsAccepted()
    {
        // ops/appservice/appservice-settings.md: same-origin SPAs, Azure host as root, any-network forwarded headers.
        var problems = Problems(v =>
        {
            v["Tenancy:Mode"] = "SingleHost";
            v["Tenancy:RootDomain"] = "crownus-api.uksouth-01.azurewebsites.net";
            v["Urls:AppBaseUrl"] = "https://crownus-api.uksouth-01.azurewebsites.net";
            v["Urls:AdminConsoleBaseUrl"] = "https://crownus-api.uksouth-01.azurewebsites.net/admin-console";
            v["ForwardedHeaders:KnownNetworks:0"] = "0.0.0.0/0";
            v["ForwardedHeaders:KnownNetworks:1"] = "::/0";
        });

        problems.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ConnectionStrings:DefaultConnection")]
    [InlineData("Encryption:MasterKey")]
    [InlineData("Email:FromAddress")]
    [InlineData("Tenancy:RootDomain")]
    [InlineData("Urls:AppBaseUrl")]
    [InlineData("Urls:AdminConsoleBaseUrl")]
    public void RequiredSetting_Missing_IsReportedByKey(string key)
    {
        Problems(v => v.Remove(key)).Should().ContainSingle().Which.Should().StartWith(key);
        Problems(v => v[key] = "<from-user>").Should().ContainSingle().Which.Should().StartWith(key);
        Problems(v => v[key] = "CHANGE_ME").Should().ContainSingle().Which.Should().StartWith(key);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("crownus.test")]
    [InlineData("app.example.com")]
    [InlineData("dev.localhost")]
    [InlineData("127.0.0.1")]
    public void RootDomain_LocalOrExampleHost_IsRejected(string host)
        => Problems(v => v["Tenancy:RootDomain"] = host).Should().ContainSingle()
            .Which.Should().Contain("Tenancy:RootDomain");

    [Theory]
    [InlineData("https://crownus-uat.com")]
    [InlineData("crownus-uat.com:443")]
    public void RootDomain_WithSchemeOrPort_IsRejected(string value)
        => Problems(v => v["Tenancy:RootDomain"] = value).Should().ContainSingle()
            .Which.Should().Contain("bare host name");

    [Theory]
    [InlineData("http://crownus-uat.com", "https")]
    [InlineData("https://localhost:4200", "local or example")]
    [InlineData("https://app.example.com", "local or example")]
    [InlineData("crownus-uat.com", "absolute URL")]
    public void AppBaseUrl_Invalid_IsRejected(string value, string expected)
        => Problems(v => v["Urls:AppBaseUrl"] = value).Should().ContainSingle()
            .Which.Should().Contain("Urls:AppBaseUrl").And.Contain(expected);

    [Fact]
    public void OptionalUrl_Empty_IsFine_ButPlaceholderOrHttpIsNot()
    {
        Problems(v => v["Urls:WebhookBaseUrl"] = "").Should().BeEmpty();
        Problems(v => v["Urls:WebhookBaseUrl"] = "<host>").Should().ContainSingle().Which.Should().Contain("placeholder");
        Problems(v => v["Urls:WebhookBaseUrl"] = "http://crownus-uat.com").Should().ContainSingle().Which.Should().Contain("https");
    }

    [Fact]
    public void AllowedOrigins_OldPlaceholders_AreRejected()
    {
        var problems = Problems(v =>
        {
            v["AllowedOrigins:0"] = "http://localhost:5173";
            v["AllowedOrigins:1"] = "https://app.example.com";
            v["AllowedOrigins:2"] = "https://tools.crownus-uat.com";
        });

        problems.Should().HaveCount(2);
        problems.Should().Contain(p => p.StartsWith("AllowedOrigins:0"));
        problems.Should().Contain(p => p.StartsWith("AllowedOrigins:1"));
    }

    [Fact]
    public void MasterKey_TooShort_IsRejected()
        => Problems(v => v["Encryption:MasterKey"] = "short-key").Should().ContainSingle()
            .Which.Should().Contain("at least 32");

    [Fact]
    public void AdminCookieDomain_Local_IsRejected_EmptyIsFine()
    {
        Problems(v => v["AdminCookieDomain"] = ".crownus.test").Should().ContainSingle().Which.Should().StartWith("AdminCookieDomain");
        Problems(v => v["AdminCookieDomain"] = "").Should().BeEmpty();
        Problems(v => v["AdminCookieDomain"] = ".crownus-uat.com").Should().BeEmpty();
    }

    [Fact]
    public void ForwardedHeaders_Unparseable_AreRejected()
    {
        var problems = Problems(v =>
        {
            v["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5";
            v["ForwardedHeaders:KnownProxies:1"] = "load-balancer";
            v["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/16";
            v["ForwardedHeaders:KnownNetworks:1"] = "10.0.0.0";
        });

        problems.Should().HaveCount(2);
        problems.Should().Contain(p => p.StartsWith("ForwardedHeaders:KnownProxies:1"));
        problems.Should().Contain(p => p.StartsWith("ForwardedHeaders:KnownNetworks:1"));
    }

    [Fact]
    public void Swagger_On_IsRejected()
        => Problems(v => v["Swagger:Enabled"] = "true").Should().ContainSingle().Which.Should().StartWith("Swagger:Enabled");

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Testing")]
    public void LocalEnvironments_AreNotChecked(string environment)
        => DeploymentConfigurationValidator.Validate(Config(new()), environment);

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void DeployedEnvironment_WithProblems_Throws_ListingKeysNotValues(string environment)
    {
        var values = Valid();
        values["Urls:AppBaseUrl"] = "http://crownus-uat.com";
        values.Remove("Email:FromAddress");

        var act = () => DeploymentConfigurationValidator.Validate(Config(values), environment);

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("2 setting(s)").And.Contain("Urls:AppBaseUrl").And.Contain("Email:FromAddress")
            .And.Contain(environment);
        message.Should().NotContain(Secret).And.NotContain("Password=x");
    }

    [Fact]
    public void DeployedEnvironment_Valid_DoesNotThrow()
        => DeploymentConfigurationValidator.Validate(Config(Valid()), "Staging");
}
