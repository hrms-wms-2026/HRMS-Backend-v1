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
    public void ValidateSignature_UnrecognizedExtension_IsRejected()
    {
        // Task 23: fail closed. Every allow-listed extension has a check, so an extension with none
        // can only be something the allow-list never meant to accept.
        using var content = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var result = _policy.ValidateSignature("notes.unknownext", content);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
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

    // ── Task 23 ──────────────────────────────────────────────────────────────

    private static readonly byte[] WindowsExe = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 };

    [Theory]
    [InlineData("invoice.png")]
    [InlineData("invoice.jpg")]
    [InlineData("invoice.webp")]
    [InlineData("invoice.pdf")]
    [InlineData("invoice.docx")]
    [InlineData("invoice.mp4")]
    [InlineData("invoice.mp3")]
    [InlineData("invoice.wav")]
    [InlineData("invoice.ogg")]
    [InlineData("invoice.webm")]
    [InlineData("invoice.csv")]
    public void ValidateSignature_ExeRenamedToAnAllowedExtension_IsRejected(string fileName)
    {
        using var content = new MemoryStream(WindowsExe);

        var result = _policy.ValidateSignature(fileName, content);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Theory]
    [InlineData("clip.mp4", new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34, 0x32 })]
    [InlineData("clip.mov", new byte[] { 0, 0, 0, 0x14, 0x66, 0x74, 0x79, 0x70, 0x71, 0x74, 0x20, 0x20 })]
    [InlineData("voice.m4a", new byte[] { 0, 0, 0, 0x20, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x34, 0x41, 0x20 })]
    [InlineData("clip.webm", new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81 })]
    [InlineData("voice.ogg", new byte[] { 0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0x00, 0x00 })]
    [InlineData("voice.wav", new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x08, 0, 0, 0x57, 0x41, 0x56, 0x45 })]
    [InlineData("song.mp3", new byte[] { 0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData("song-no-tag.mp3", new byte[] { 0xFF, 0xFB, 0x90, 0x64, 0x00, 0x00, 0x00, 0x00 })]
    public void ValidateSignature_RealMediaHeaders_AreAccepted(string fileName, byte[] header)
    {
        using var content = new MemoryStream(header);

        Assert.True(_policy.ValidateSignature(fileName, content).IsSuccess);
    }

    [Fact]
    public void ValidateSignature_CsvText_IsAccepted()
    {
        using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("\uFEFFfirst_name,last_name\nAda,Lovelace\n"));

        Assert.True(_policy.ValidateSignature("employees.csv", content).IsSuccess);
    }

    [Fact]
    public void ValidateSignature_XlsxRenamedToCsv_IsRejected()
    {
        using var content = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00 });

        Assert.False(_policy.ValidateSignature("employees.csv", content).IsSuccess);
    }

    [Fact]
    public void GenerateStorageKey_UsesAServerGeneratedObjectName()
    {
        var tenantId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var safeFileName = _policy.SanitizeFileName("Passport scan (Ada).pdf");

        var key = _policy.GenerateStorageKey(tenantId, reservationId, "employee_document", safeFileName);

        Assert.Equal($"tenants/{tenantId:N}/employee_document/{reservationId:N}/content.pdf", key);
    }
}
