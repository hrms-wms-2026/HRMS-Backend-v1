using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.Legal.Services;

public class LegalAcceptanceChecker : ILegalAcceptanceChecker
{
    private readonly ILegalDocumentVersionRepository _versionRepository;
    private readonly ILegalAcceptanceRepository _acceptanceRepository;
    private readonly IDateTimeProvider _clock;

    public LegalAcceptanceChecker(
        ILegalDocumentVersionRepository versionRepository,
        ILegalAcceptanceRepository acceptanceRepository,
        IDateTimeProvider clock)
    {
        _versionRepository = versionRepository;
        _acceptanceRepository = acceptanceRepository;
        _clock = clock;
    }

    public async Task<LegalAcceptanceCheckResult> CheckAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var allRequiredVersions = await _versionRepository.GetCurrentRequiredVersionsAsync(ct);

        var now = _clock.UtcNow;
        var dashboardRequiredVersions = allRequiredVersions
            .Where(v => string.Equals(v.Status, "published", StringComparison.OrdinalIgnoreCase))
            .Where(v => v.IsRequired)
            .Where(v => string.Equals(v.BlockScope, "dashboard", StringComparison.OrdinalIgnoreCase))
            .Where(v => v.PublishedAt.HasValue && v.PublishedAt.Value <= now)
            .ToList();

        if (dashboardRequiredVersions.Count == 0)
        {
            return new LegalAcceptanceCheckResult(
                Status: LegalAcceptanceStatus.NotConfigured,
                IsComplete: false,
                PendingDocuments: [],
                ErrorCode: "MissingRequiredLegalVersions");
        }

        var userAcceptances = await _acceptanceRepository.GetUserAcceptancesAsync(tenantId, userId, ct);

        var pendingDocs = new List<PendingLegalDocumentDto>();

        foreach (var requiredVer in dashboardRequiredVersions)
        {
            var isSatisfied = userAcceptances.Any(a =>
                string.Equals(a.DocumentType, requiredVer.DocumentType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.DocumentVersion, requiredVer.Version, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(a.Decision, "accepted", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(a.Decision, "acknowledged", StringComparison.OrdinalIgnoreCase)));

            if (!isSatisfied)
            {
                pendingDocs.Add(new PendingLegalDocumentDto(
                    DocumentType: requiredVer.DocumentType,
                    Version: requiredVer.Version,
                    Title: requiredVer.Title,
                    EffectiveAt: requiredVer.PublishedAt,
                    ContentUrl: requiredVer.ContentUrl));
            }
        }

        if (pendingDocs.Count > 0)
        {
            return new LegalAcceptanceCheckResult(
                Status: LegalAcceptanceStatus.Pending,
                IsComplete: false,
                PendingDocuments: pendingDocs);
        }

        return new LegalAcceptanceCheckResult(
            Status: LegalAcceptanceStatus.Complete,
            IsComplete: true,
            PendingDocuments: []);
    }
}
