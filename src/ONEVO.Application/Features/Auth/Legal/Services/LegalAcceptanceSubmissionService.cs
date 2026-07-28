using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.Auth.Legal.Services;

public sealed class LegalAcceptanceSubmissionService : ILegalAcceptanceSubmissionService
{
    private readonly ILegalDocumentVersionRepository _versions;
    private readonly ILegalAcceptanceRepository _acceptances;
    private readonly IDateTimeProvider _clock;

    public LegalAcceptanceSubmissionService(
        ILegalDocumentVersionRepository versions,
        ILegalAcceptanceRepository acceptances,
        IDateTimeProvider clock)
    {
        _versions = versions;
        _acceptances = acceptances;
        _clock = clock;
    }

    public async Task<Result<bool>> ValidateAndStageAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyList<LegalAcceptanceItemInput> acceptances,
        bool requireComplete,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        var currentRequired = (await _versions.GetCurrentRequiredVersionsAsync(ct))
            .GroupBy(v => v.DocumentType, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(v => v.PublishedAt).First())
            .ToList();

        if (currentRequired.Count == 0)
            return Result<bool>.Success(true);

        if (acceptances is null || acceptances.Count == 0)
            return Result<bool>.Failure("Required legal acceptances must be provided.", 400);

        var duplicateType = acceptances
            .GroupBy(a => a.DocumentType, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateType is not null)
            return Result<bool>.Failure(
                $"Document type '{duplicateType.Key}' was submitted more than once.", 400);

        var requiredByType = currentRequired.ToDictionary(
            v => v.DocumentType,
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in acceptances)
        {
            if (!requiredByType.TryGetValue(item.DocumentType, out var current)
                || !string.Equals(current.Version, item.Version, StringComparison.OrdinalIgnoreCase))
            {
                return Result<bool>.Failure(
                    $"Document type '{item.DocumentType}' version '{item.Version}' is not the current required version.",
                    400);
            }

            if (!string.IsNullOrWhiteSpace(item.ContentHash)
                && !string.Equals(item.ContentHash, current.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return Result<bool>.Failure(
                    $"content_hash for '{item.DocumentType}' version '{item.Version}' does not match the current published content.",
                    409);
            }

            var validDecision =
                string.Equals(item.Decision, "accepted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Decision, "acknowledged", StringComparison.OrdinalIgnoreCase);
            if (!validDecision)
            {
                return Result<bool>.Failure(
                    $"Invalid decision '{item.Decision}' for document '{item.DocumentType}'.", 400);
            }
        }

        if (requireComplete)
        {
            var submittedTypes = acceptances
                .Select(a => a.DocumentType)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = currentRequired.FirstOrDefault(v => !submittedTypes.Contains(v.DocumentType));
            if (missing is not null)
                return Result<bool>.Failure(
                    $"Acceptance for required document '{missing.DocumentType}' is missing.", 400);
        }

        var now = _clock.UtcNow;
        foreach (var item in acceptances)
        {
            var current = requiredByType[item.DocumentType];
            await _acceptances.AddAsync(new LegalAcceptanceRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                DocumentType = current.DocumentType,
                DocumentVersion = current.Version,
                Decision = item.Decision.ToLowerInvariant(),
                Required = true,
                DecidedAt = now,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Source = "web"
            }, ct);
        }

        return Result<bool>.Success(true);
    }
}
