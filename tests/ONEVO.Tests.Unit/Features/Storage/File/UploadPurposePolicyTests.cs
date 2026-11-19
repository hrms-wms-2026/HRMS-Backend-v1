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

    [Theory]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("clip.mov", "video/quicktime")]
    [InlineData("clip.webm", "video/webm")]
    [InlineData("voice.mp3", "audio/mpeg")]
    [InlineData("voice.m4a", "audio/x-m4a")]
    [InlineData("voice.wav", "audio/wav")]
    [InlineData("voice.ogg", "audio/ogg")]
    public void ValidateUpload_AcceptsVideoAndAudioInChats(string fileName, string contentType)
    {
        var result = _policy.ValidateUpload("conversation_attachment", fileName, contentType, 1024);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public void ValidateUpload_KeepsVideoOutOfTaskAttachments()
    {
        var result = _policy.ValidateUpload("task_attachment", "clip.mp4", "video/mp4", 1024);

        Assert.False(result.IsSuccess);
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

    // SEC-13 (2026-10-12): magic-number signature verification, additive on top of the existing
    // extension+content-type check above - never narrows what ValidateUpload already allows.

    [Fact]
    public void ValidateSignature_PngBytesWithPngExtension_IsAccepted()
    {
        using var content = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 });

        var result = _policy.ValidateSignature("photo.png", content);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateSignature_PngBytesWithPdfExtension_IsRejected()
    {
        // A file renamed to look like a different, allowed type - exactly the SEC-13 scenario.
        using var content = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 });

        var result = _policy.ValidateSignature("report.pdf", content);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public void ValidateSignature_ZipSignatureWithDocxExtension_IsAccepted()
    {
        // .docx is a ZIP archive at the byte level - this must NOT be treated as a mismatch.
        using var content = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0 });

        var result = _policy.ValidateSignature("budget.docx", content);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateSignature_ZipSignatureWithXlsxExtension_IsAccepted()
    {
        using var content = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0 });

        var result = _policy.ValidateSignature("budget.xlsx", content);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateSignature_UnrecognizedExtension_IsAcceptedWithoutGuessing()
    {
        // No entry in the signature table for this extension - handled explicitly by doing
        // nothing here, not by rejecting an ambiguous/unlisted format outright.
        using var content = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var result = _policy.ValidateSignature("notes.unknownext", content);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateSignature_WebpRequiresBothRiffAndWebpTags()
    {
        // RIFF container but a different fourcc (e.g. a WAV file renamed to .webp) must be rejected.
        using var content = new MemoryStream(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 });

        var result = _policy.ValidateSignature("clip.webp", content);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ValidateSignature_ValidWebp_IsAccepted()
    {
        using var content = new MemoryStream(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 });

        var result = _policy.ValidateSignature("photo.webp", content);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateSignature_LeavesStreamPositionUnchangedForTheCaller()
    {
        using var content = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 });
        content.Position = 5;

        _policy.ValidateSignature("photo.png", content);

        Assert.Equal(5, content.Position);
    }

    [Fact]
    public void DetectContentType_PngBytes_ReturnsImagePng()
    {
        using var content = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        Assert.Equal("image/png", UploadPurposePolicy.DetectContentType("photo.png", content));
    }

    [Fact]
    public void DetectContentType_UnrecognizedExtension_ReturnsNull()
    {
        using var content = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        Assert.Null(UploadPurposePolicy.DetectContentType("notes.unknownext", content));
    }
}
