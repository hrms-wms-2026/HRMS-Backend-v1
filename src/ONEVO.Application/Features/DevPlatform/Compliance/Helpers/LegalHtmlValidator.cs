using ONEVO.Application.Common.Helpers;

namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>
/// Allowlist-first legal document HTML validator. Any tag not explicitly allowed is rejected,
/// and any attribute not explicitly allowed for its tag is rejected - this is what actually
/// blocks &lt;script&gt;, on*= handlers, javascript: URLs, and &lt;iframe|object|embed&gt; (none of
/// them appear in the shared allowlist in BasicRichHtml), rather than trying to blacklist each one individually.
/// </summary>
public static class LegalHtmlValidator
{
    public sealed record LegalHtmlValidationResult(bool IsValid, string? ErrorMessage);

    /// <summary>Legal content must not be empty and must pass the shared rich-text allowlist
    /// (<see cref="BasicRichHtml"/>, the same list the editor's basic toolset produces).</summary>
    public static LegalHtmlValidationResult Validate(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new LegalHtmlValidationResult(false, "content_html must not be empty.");
        }

        var check = BasicRichHtml.Validate(html, "legal document content");
        return new LegalHtmlValidationResult(check.IsValid, check.Error);
    }
}
