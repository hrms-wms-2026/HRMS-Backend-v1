using System.Text.Json;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Definitions;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class ServiceKeyDefinitionTests
{
    private static Dictionary<string, string> R2Fields() => new()
    {
        ["accountId"] = "a",
        ["bucketName"] = "b",
        ["accessKeyId"] = "k",
        ["secretAccessKey"] = "s",
        ["endpoint"] = "https://a.r2.cloudflarestorage.com"
    };

    [Fact]
    public void EverySupportedServiceKey_HasADefinition()
    {
        foreach (var serviceKey in PlatformServiceKeyCatalog.SupportedServiceKeys)
            Assert.NotNull(ServiceKeyDefinitionRegistry.Find(serviceKey));
    }

    [Fact]
    public void SingleFieldDefinition_StoresTheRawValue_SoExistingRowsAreUnchanged()
    {
        var result = ServiceKeyDefinitionRegistry.BuildCredential(
            PlatformServiceKeyCatalog.Resend, null, new Dictionary<string, string> { ["apiKey"] = "  re_abc  " });

        Assert.True(result.IsSuccess);
        Assert.Equal("re_abc", result.Value);
    }

    [Fact]
    public void LegacyApiKeyShape_StillWorksForSingleFieldProviders()
    {
        var result = ServiceKeyDefinitionRegistry.BuildCredential(
            PlatformServiceKeyCatalog.Sendgrid, "SG.legacy", null);

        Assert.True(result.IsSuccess);
        Assert.Equal("SG.legacy", result.Value);
    }

    [Fact]
    public void AwsRekognition_Fields_BecomeAJsonBundleWithFieldNames()
    {
        var result = ServiceKeyDefinitionRegistry.BuildCredential(
            PlatformServiceKeyCatalog.AwsRekognition, null, new Dictionary<string, string>
            {
                ["accessKeyId"] = " AKIAEXAMPLE ",
                ["secretAccessKey"] = "secret-value",
                ["region"] = "eu-west-2"
            });

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(result.Value!);
        Assert.Equal("AKIAEXAMPLE", doc.RootElement.GetProperty("accessKeyId").GetString());
        Assert.Equal("secret-value", doc.RootElement.GetProperty("secretAccessKey").GetString());
        Assert.Equal("eu-west-2", doc.RootElement.GetProperty("region").GetString());
    }

    [Fact]
    public void AwsRekognition_RegionOutsideTheOptions_IsRejected()
    {
        var result = ServiceKeyDefinitionRegistry.BuildCredential(
            PlatformServiceKeyCatalog.AwsRekognition, null, new Dictionary<string, string>
            {
                ["accessKeyId"] = "AKIA",
                ["secretAccessKey"] = "s",
                ["region"] = "mars-north-1"
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void MissingRequiredField_IsRejected_NamingTheField()
    {
        var fields = R2Fields();
        fields.Remove("secretAccessKey");

        var result = ServiceKeyDefinitionRegistry.BuildCredential(
            PlatformServiceKeyCatalog.CloudflareR2, null, fields);

        Assert.False(result.IsSuccess);
        Assert.Contains("Secret Access Key", result.Error);
    }

    [Fact]
    public void R2_OptionalRegion_DefaultsToAuto()
    {
        var result = ServiceKeyDefinitionRegistry.BuildCredential(
            PlatformServiceKeyCatalog.CloudflareR2, null, R2Fields());

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(result.Value!);
        Assert.Equal("auto", doc.RootElement.GetProperty("region").GetString());
    }

    [Fact]
    public void R2_EndpointMustBeAUrl()
    {
        var fields = R2Fields();
        fields["endpoint"] = "not a url";

        var result = ServiceKeyDefinitionRegistry.BuildCredential(
            PlatformServiceKeyCatalog.CloudflareR2, null, fields);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void UnknownField_IsRejected()
    {
        var result = ServiceKeyDefinitionRegistry.BuildCredential(
            PlatformServiceKeyCatalog.Resend, null, new Dictionary<string, string>
            {
                ["apiKey"] = "re_x",
                ["bogus"] = "1"
            });

        Assert.False(result.IsSuccess);
        Assert.Contains("bogus", result.Error);
    }

    [Fact]
    public void LegacyR2JsonPastedIntoApiKey_IsAcceptedAndNormalised()
    {
        const string pasted =
            "{\"AccountId\":\"a\",\"BucketName\":\"b\",\"AccessKeyId\":\"k\",\"SecretAccessKey\":\"s\",\"Endpoint\":\"https://a.r2.cloudflarestorage.com\"}";

        var result = ServiceKeyDefinitionRegistry.BuildCredential(
            PlatformServiceKeyCatalog.CloudflareR2, pasted, null);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(result.Value!);
        Assert.Equal("a", doc.RootElement.GetProperty("accountId").GetString());
    }

    [Fact]
    public void BundleProvider_RejectsAPlainStringInApiKey()
    {
        var result = ServiceKeyDefinitionRegistry.BuildCredential(
            PlatformServiceKeyCatalog.AwsRekognition, "just-a-string", null);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ProviderWithoutADefinition_IsRejected()
    {
        var result = ServiceKeyDefinitionRegistry.BuildCredential("brand_new", "x", null);

        Assert.False(result.IsSuccess);
        Assert.Contains("brand_new", result.Error);
    }
}
