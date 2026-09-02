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
    public void ValidateUpload_RejectsContentTypeAndExtensionMismatch()
    {
        var result = _policy.ValidateUpload("employee_avatar", "photo.jpg", "image/png", 1024);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_AcceptsValidAvatarUpload()
    {
        var result = _policy.ValidateUpload("employee_avatar", "photo.png", "image/png", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_AcceptsValidObjectiveAssetZipUpload()
    {
        var result = _policy.ValidateUpload("objective_asset", "archive.zip", "application/zip", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_AcceptsZipReportedAsXZipCompressed()
    {
        // Chrome on Windows commonly reports this instead of application/zip.
        var result = _policy.ValidateUpload("objective_asset", "archive.zip", "application/x-zip-compressed", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_AcceptsValidObjectiveAssetXlsxUpload()
    {
        var result = _policy.ValidateUpload(
            "objective_asset", "budget.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_AcceptsXlsxReportedAsOctetStream()
    {
        // Browsers frequently fall back to this generic type for .xls/.xlsx.
        var result = _policy.ValidateUpload("objective_asset", "budget.xlsx", "application/octet-stream", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_AcceptsValidObjectiveAssetXlsUpload()
    {
        var result = _policy.ValidateUpload("objective_asset", "budget.xls", "application/vnd.ms-excel", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_AcceptsValidObjectiveAssetGifUpload()
    {
        var result = _policy.ValidateUpload("objective_asset", "diagram.gif", "image/gif", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_RejectsOctetStreamForNonRiskyExtension()
    {
        // application/octet-stream is only an accepted fallback for .zip/.xls/.xlsx,
        // not for every extension — a .pdf claiming octet-stream must still fail.
        var result = _policy.ValidateUpload("objective_asset", "plan.pdf", "application/octet-stream", 1024);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateUpload_RejectsOversizedObjectiveAsset()
    {
        var result = _policy.ValidateUpload("objective_asset", "huge.zip", "application/zip", 26 * 1024 * 1024);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GenerateStorageKey_NeverContainsClientProvidedDirectoryTraversal()
    {
        var tenantId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var safeFileName = _policy.SanitizeFileName("../../etc/passwd.png");
        var key = _policy.GenerateStorageKey(tenantId, reservationId, "employee_avatar", safeFileName);

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
