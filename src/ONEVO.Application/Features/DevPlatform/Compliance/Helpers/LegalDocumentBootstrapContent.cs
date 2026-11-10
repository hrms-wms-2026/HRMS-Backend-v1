namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>
/// Single source of truth for the Phase 1 bootstrap legal document bodies. The migration
/// (backfill SQL), the dev seeder, and the hasher unit tests all read from these constants
/// so the stored content_hash can never drift from what actually gets persisted.
/// </summary>
public static class LegalDocumentBootstrapContent
{
    public const string TermsHtml =
        "<h1>Crownus Terms and Conditions</h1><p>These are the Crownus Terms and Conditions (Bootstrap Dev). By using Crownus, you agree to the terms described in this document. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.</p>";

    public const string TermsText =
        "Crownus Terms and Conditions\n\nThese are the Crownus Terms and Conditions (Bootstrap Dev). By using Crownus, you agree to the terms described in this document. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.";

    public const string TermsJson =
        "{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":1},\"content\":[{\"type\":\"text\",\"text\":\"Crownus Terms and Conditions\"}]},{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"These are the Crownus Terms and Conditions (Bootstrap Dev). By using Crownus, you agree to the terms described in this document. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.\"}]}]}";

    public const string PrivacyHtml =
        "<h1>Crownus Privacy Notice</h1><p>This is the Crownus Privacy Notice (Bootstrap Dev). It describes, at a placeholder level, how Crownus collects, uses, and protects personal data. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.</p>";

    public const string PrivacyText =
        "Crownus Privacy Notice\n\nThis is the Crownus Privacy Notice (Bootstrap Dev). It describes, at a placeholder level, how Crownus collects, uses, and protects personal data. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.";

    public const string PrivacyJson =
        "{\"type\":\"doc\",\"content\":[{\"type\":\"heading\",\"attrs\":{\"level\":1},\"content\":[{\"type\":\"text\",\"text\":\"Crownus Privacy Notice\"}]},{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"This is the Crownus Privacy Notice (Bootstrap Dev). It describes, at a placeholder level, how Crownus collects, uses, and protects personal data. This placeholder content represents the Phase 1 legal baseline and will be replaced with finalized legal text before general availability.\"}]}]}";

    /// <summary>Defensive fallback for any row that is neither the terms nor privacy_notice bootstrap row.</summary>
    public const string GenericFallbackHtml =
        "<p>This legal document version was created before rich content storage was enabled. Placeholder content has been applied; please edit and republish with the final legal text.</p>";

    public const string GenericFallbackText =
        "This legal document version was created before rich content storage was enabled. Placeholder content has been applied; please edit and republish with the final legal text.";

    public const string GenericFallbackJson =
        "{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"This legal document version was created before rich content storage was enabled. Placeholder content has been applied; please edit and republish with the final legal text.\"}]}]}";
}
