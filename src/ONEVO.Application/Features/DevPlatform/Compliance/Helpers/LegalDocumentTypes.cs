namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>Phase 1 supported legal document types only. Do not add new types here without a spec update.</summary>
public static class LegalDocumentTypes
{
    public static readonly IReadOnlyCollection<string> Allowed = new[] { "terms", "privacy_notice" };
}
