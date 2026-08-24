using ONEVO.Application.Features.Storage.File.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public class UploadPurposeCatalogTests
{
    [Fact]
    public void ProjectBanner_ResolvesToSameRuleShapeAsProjectCover()
    {
        Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.ProjectBanner));

        var banner = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.ProjectBanner);
        var cover = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.ProjectCover);

        Assert.NotNull(banner);
        Assert.NotNull(cover);
        Assert.Equal(cover.MaxSizeBytes, banner.MaxSizeBytes);
        Assert.Equal(cover.AllowedContentTypes, banner.AllowedContentTypes);
        Assert.Equal(cover.AllowedExtensions, banner.AllowedExtensions);
    }
}
