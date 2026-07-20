using ONEVO.Infrastructure.Services.Storage.File;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage.File;

public class UploadPurposePolicyTests
{
    private readonly UploadPurposePolicy _policy = new();

    [Fact]
    public void ValidateUpload_RejectsUnsupportedPurpose()
    {
        var result = _policy.ValidateUpload("not_a_real_purpose", "photo.png", "image/png", 1024);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void ValidateUpload_RejectsZeroSize()
    {
        var result = _policy.ValidateUpload("employee_avatar", "photo.png", "image/png", 0);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_RejectsOversizedFile()
    {
        var result = _policy.ValidateUpload("employee_avatar", "photo.png", "image/png", 10 * 1024 * 1024);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_RejectsDisallowedContentType()
    {
        var result = _policy.ValidateUpload("employee_avatar", "malware.exe", "application/x-msdownload", 1024);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_RejectsDisallowedExtension()
    {
        var result = _policy.ValidateUpload("employee_avatar", "photo.bmp", "image/png", 1024);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_AcceptsValidAvatarUpload()
    {
        var result = _policy.ValidateUpload("employee_avatar", "photo.png", "image/png", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void GenerateStorageKey_NeverContainsClientProvidedDirectoryTraversal()
    {
        var tenantId = Guid.NewGuid();
        var safeFileName = _policy.SanitizeFileName("../../etc/passwd.png");
        var key = _policy.GenerateStorageKey(tenantId, "employee_avatar", safeFileName);

        Assert.DoesNotContain("..", key);
        Assert.DoesNotContain("/etc/", key);
        Assert.StartsWith($"tenants/{tenantId:N}/employee_avatar/", key);
    }

    [Fact]
    public void SanitizeFileName_StripsUnsafeCharacters()
    {
        var safe = _policy.SanitizeFileName("my photo!@#$.png");

        Assert.DoesNotContain(" ", safe);
        Assert.DoesNotContain("!", safe);
        Assert.EndsWith(".png", safe);
    }
}
