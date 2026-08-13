using ONEVO.Application.Features.Storage.File.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public class UploadPurposeCatalogTests
{
    [Fact]
    public void MonitoringFaceLiveness_IsSupported_WithImageOnlyRule()
    {
        Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.MonitoringFaceLiveness));

        var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.MonitoringFaceLiveness);

        Assert.NotNull(rule);
        Assert.Equal(5 * 1024 * 1024, rule!.MaxSizeBytes);
        Assert.Contains("image/jpeg", rule.AllowedContentTypes);
        Assert.DoesNotContain("application/pdf", rule.AllowedContentTypes);
    }
}
