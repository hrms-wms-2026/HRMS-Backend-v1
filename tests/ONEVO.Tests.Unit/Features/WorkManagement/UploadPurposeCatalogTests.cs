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

    [Fact]
    public void TaskAttachment_IsSupported_AllowsBroadDocumentTypes()
    {
        Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.TaskAttachment));
        var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.TaskAttachment)!;
        Assert.Equal(25 * 1024 * 1024, rule.MaxSizeBytes);
        Assert.Contains("application/zip", rule.AllowedContentTypes);
        Assert.Contains(".xlsx", rule.AllowedExtensions);
    }

    [Fact]
    public void TaskDescriptionImage_IsSupported_ImageOnlyFiveMegabytes()
    {
        Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.TaskDescriptionImage));
        var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.TaskDescriptionImage)!;
        Assert.Equal(5 * 1024 * 1024, rule.MaxSizeBytes);
        Assert.DoesNotContain("application/pdf", rule.AllowedContentTypes);
    }

    [Fact]
    public void CommentAttachment_IsSupported_MatchesTaskAttachmentRule()
    {
        Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.CommentAttachment));
        var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.CommentAttachment)!;
        Assert.Equal(25 * 1024 * 1024, rule.MaxSizeBytes);
        Assert.Contains("application/zip", rule.AllowedContentTypes);
    }

    [Fact]
    public void CommentDescriptionImage_IsSupported_ImageOnlyFiveMegabytes()
    {
        Assert.True(UploadPurposeCatalog.IsSupported(UploadPurposeCatalog.CommentDescriptionImage));
        var rule = UploadPurposeCatalog.GetRule(UploadPurposeCatalog.CommentDescriptionImage)!;
        Assert.Equal(5 * 1024 * 1024, rule.MaxSizeBytes);
        Assert.DoesNotContain("application/pdf", rule.AllowedContentTypes);
    }
}
