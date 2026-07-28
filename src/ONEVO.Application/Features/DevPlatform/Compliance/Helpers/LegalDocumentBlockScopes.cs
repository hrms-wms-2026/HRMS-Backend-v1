namespace ONEVO.Application.Features.DevPlatform.Compliance.Helpers;

/// <summary>Phase 1 supported block scopes only, for versions created/updated via the admin API.</summary>
public static class LegalDocumentBlockScopes
{
    public static readonly IReadOnlyCollection<string> Allowed = new[] { "dashboard" };
}
