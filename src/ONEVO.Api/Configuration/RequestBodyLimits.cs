namespace ONEVO.Api.Configuration;

/// <summary>
/// Task 23: request body limits for the endpoints that need more than the 1 MB default
/// (<see cref="ONEVO.Api.Middleware.RequestBodySizeLimitMiddleware.DefaultMaxBodyBytes"/>). Each one is
/// the largest file the upload purpose allows (UploadPurposeCatalog) plus about 1 MB for the
/// multipart envelope and form fields. The full per-endpoint table is in
/// docs/reports/security/2026-11-18-task-23-headers-uploads-limits.md.
/// </summary>
public static class RequestBodyLimits
{
    private const long MiB = 1024 * 1024;

    /// <summary>25 MiB documents and attachments (objective assets, task/comment attachments).</summary>
    public const long DocumentUpload = 26 * MiB;

    /// <summary>Project create with an optional 5 MiB logo and an optional 5 MiB banner.</summary>
    public const long ProjectCreateWithImages = 11 * MiB;

    /// <summary>Bulk onboarding CSV/XLSX. At most 200 rows (CsvBatchParser.MaxRows), so 5 MiB is generous.</summary>
    public const long BulkOnboardingFile = 6 * MiB;
    public const long BulkOnboardingFileMaxBytes = 5 * MiB;

    /// <summary>App catalog logo: the handler allows 2 MiB.</summary>
    public const long AppCatalogLogo = 3 * MiB;

    /// <summary>Legal documents carry the same text three times (JSON, HTML and plain text).</summary>
    public const long LegalDocumentVersion = 4 * MiB;

    /// <summary>Stripe event payloads are small, but a failed webhook is retried forever, so leave headroom.</summary>
    public const long StripeWebhook = 2 * MiB;
}
