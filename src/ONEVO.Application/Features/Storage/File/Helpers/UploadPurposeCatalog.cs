namespace ONEVO.Application.Features.Storage.File.Helpers;

public sealed record UploadPurposeRule(
    long MaxSizeBytes,
    IReadOnlyList<string> AllowedContentTypes,
    IReadOnlyList<string> AllowedExtensions);

public static class UploadPurposeCatalog
{
    public const string CompanyLogo = "company_logo";
    public const string EmployeeAvatar = "employee_avatar";
    public const string GenericDocument = "generic_document";
    public const string MonitoringScreenshot = "monitoring_screenshot";
    public const string IdentityVerificationPhoto = "identity_verification_photo";

    private static readonly IReadOnlyList<string> ImageContentTypes = new[]
    {
        "image/png", "image/jpeg", "image/webp"
    };

    private static readonly IReadOnlyList<string> ImageExtensions = new[]
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    private static readonly Dictionary<string, UploadPurposeRule> Rules = new()
    {
        [CompanyLogo] = new UploadPurposeRule(5 * 1024 * 1024, ImageContentTypes, ImageExtensions),
        [EmployeeAvatar] = new UploadPurposeRule(5 * 1024 * 1024, ImageContentTypes, ImageExtensions),
        [MonitoringScreenshot] = new UploadPurposeRule(
            5 * 1024 * 1024,
            new[] { "image/png", "image/jpeg" },
            new[] { ".png", ".jpg", ".jpeg" }),
        [IdentityVerificationPhoto] = new UploadPurposeRule(
            5 * 1024 * 1024,
            new[] { "image/png", "image/jpeg" },
            new[] { ".png", ".jpg", ".jpeg" }),
        [GenericDocument] = new UploadPurposeRule(
            25 * 1024 * 1024,
            new[]
            {
                "application/pdf", "image/png", "image/jpeg",
                "application/msword",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            },
            new[] { ".pdf", ".png", ".jpg", ".jpeg", ".doc", ".docx" })
    };

    public static IReadOnlyList<string> SupportedPurposes => Rules.Keys.ToList();

    public static bool IsSupported(string purpose)
    {
        return Rules.ContainsKey(purpose);
    }

    public static UploadPurposeRule? GetRule(string purpose)
    {
        return Rules.TryGetValue(purpose, out var rule) ? rule : null;
    }
}
